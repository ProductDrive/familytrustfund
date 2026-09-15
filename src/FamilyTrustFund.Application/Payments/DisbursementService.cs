using FamilyTrustFund.Application.Audit;
using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Application.Loans;
using FamilyTrustFund.Application.Repayments;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Payments;

namespace FamilyTrustFund.Application.Payments;

/// <summary>
/// Application service for loan disbursement: bank-recipient management
/// (including each member's settlement subaccount), Guarantor pay-per-
/// disbursement, and idempotent webhook-driven finalisation.
/// </summary>
/// <remarks>
/// Guarantors do not pre-fund the platform. A disbursement is initiated only
/// when the Guarantor pays for it: a collection is started for the gross amount
/// (approved amount + provider fee) and split to the member's subaccount, so
/// the member is settled through their subaccount once the payment is
/// confirmed. Only a provider webhook/verification confirms the payment and
/// changes the loan to DISBURSED (AGENTS §3). All financial transitions are
/// transactional and idempotent (ADR-044).
/// </remarks>
public class DisbursementService
{
    public const string DefaultProvider = "Paystack";

    /// <summary>
    /// Well-known sentinel actor used for system/provider-initiated events that
    /// have no authenticated user (e.g. payment webhooks). Audit records remain
    /// complete and auditable; the action name indicates the source.
    /// </summary>
    public static readonly Guid SystemActorId = Guid.Parse("00000000-0000-0000-0000-00000000a11a");

    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentProviderRegistry _providerRegistry;
    private readonly ICapitalFundingRepository _capitalFundingRepository;
    private readonly ILoanRepository _loanRepository;
    private readonly IFundRepository _fundRepository;
    private readonly RepaymentService _repaymentService;
    private readonly IAuditLog _auditLog;

    public DisbursementService(
        IPaymentRepository paymentRepository,
        IPaymentProviderRegistry providerRegistry,
        ICapitalFundingRepository capitalFundingRepository,
        ILoanRepository loanRepository,
        IFundRepository fundRepository,
        RepaymentService repaymentService,
        IAuditLog auditLog)
    {
        _paymentRepository = paymentRepository;
        _providerRegistry = providerRegistry;
        _capitalFundingRepository = capitalFundingRepository;
        _loanRepository = loanRepository;
        _fundRepository = fundRepository;
        _repaymentService = repaymentService;
        _auditLog = auditLog;
    }

    /// <summary>
    /// Saves a member's bank recipient (in an unverified state). If an active
    /// recipient already exists it is replaced with the new unverified one so
    /// that the changed destination is explicit before any provider verification.
    /// </summary>
    public async Task<PaymentRecipientDto> SaveRecipientAsync(
        Guid memberId,
        SaveRecipientRequest request,
        string provider = DefaultProvider,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.AccountNumber))
        {
            throw new InvalidPaymentException("Account number is required.");
        }

        var existing = await _paymentRepository.GetActiveRecipientForMemberAsync(memberId, ct);
        if (existing is not null)
        {
            existing.Disable();
        }

        var recipient = PaymentRecipient.Create(
            memberId,
            provider,
            request.BankCode,
            request.BankName,
            request.AccountNumber,
            request.AccountName);

        _paymentRepository.AddRecipient(recipient);
        await _auditLog.RecordAsync(memberId, "PaymentRecipient.Saved", "PaymentRecipient", recipient.Id,
            $"Bank={request.BankName}, AccountName={request.AccountName}", ct);
        await _paymentRepository.SaveChangesAsync(ct);

        return ToRecipientDto(recipient);
    }

    /// <summary>
    /// Verifies the member's most recently saved recipient with the provider and
    /// activates it.
    /// </summary>
    public async Task<PaymentRecipientDto> VerifyRecipientAsync(
        Guid memberId,
        CancellationToken ct = default)
    {
        var latest = await _paymentRepository.GetLatestRecipientForMemberAsync(memberId, ct)
            ?? throw new InvalidPaymentException("You have not saved bank details yet.");

        // Already verified and active — nothing to do.
        if (latest.IsActive && !string.IsNullOrWhiteSpace(latest.ProviderRecipientCode))
        {
            return ToRecipientDto(latest);
        }

        var provider = _providerRegistry.Get(latest.Provider)
            ?? throw new InvalidPaymentException($"Payment provider '{latest.Provider}' is not available.");

        var result = await provider.CreateRecipientAsync(new CreateRecipientRequest
        {
            Provider = latest.Provider,
            BankCode = latest.BankCode,
            AccountNumber = latest.AccountNumber,
            AccountName = latest.AccountName,
        }, ct);

        if (!result.Success)
        {
            throw new InvalidPaymentException($"Bank details could not be verified: {result.RejectionReason ?? "unknown error"}.");
        }

        latest.VerifyAndActivate(result.ProviderReference!);

        // The member's subaccount is how a disbursement is settled to them after
        // the Guarantor's payment is confirmed (ADR-044). Create it once per
        // verified recipient so every eligible member has a settlement path.
        if (string.IsNullOrWhiteSpace(latest.ProviderSubaccountCode))
        {
            var subaccount = await provider.CreateSubaccountAsync(new CreateSubaccountRequest
            {
                Provider = latest.Provider,
                BankCode = latest.BankCode,
                AccountNumber = latest.AccountNumber,
                AccountName = latest.AccountName,
            }, ct);

            if (!subaccount.Success || string.IsNullOrWhiteSpace(subaccount.ProviderReference))
            {
                throw new InvalidPaymentException($"A settlement subaccount could not be created for these bank details: {subaccount.RejectionReason ?? "unknown error"}.");
            }

            latest.AttachSubaccount(subaccount.ProviderReference);
        }

        await _auditLog.RecordAsync(memberId, "PaymentRecipient.Verified", "PaymentRecipient", latest.Id,
            "Recipient and settlement subaccount verified with provider", ct);
        await _paymentRepository.SaveChangesAsync(ct);

        return ToRecipientDto(latest);
    }

    /// <summary>
    /// Guarantor initiates disbursement of an approved loan. There is no
    /// pre-funded pool: this starts a collection for the gross amount
    /// (approved amount + provider fee) from the Guarantor, split to the
    /// member's settlement subaccount so the member receives the approved
    /// amount. Returns the provider checkout URL; the loan moves to
    /// DISBURSEMENT_PENDING until the payment is confirmed.
    /// </summary>
    public async Task<DisbursementDto> InitiateDisbursementAsync(
        Guid guarantorId,
        InitiateDisbursementRequest request,
        string payerEmail,
        string? callbackUrl = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(payerEmail))
        {
            throw new InvalidPaymentException("A contact email is required to pay for a disbursement.");
        }

        if (!string.IsNullOrWhiteSpace(callbackUrl)
            && (!Uri.TryCreate(callbackUrl, UriKind.Absolute, out var cb)
                || (cb.Scheme != Uri.UriSchemeHttps && cb.Scheme != Uri.UriSchemeHttp)))
        {
            throw new InvalidPaymentException("The payment return address must be a valid http(s) URL.");
        }

        var loan = await _loanRepository.GetByIdAsync(request.LoanId, ct)
            ?? throw new InvalidPaymentException("Loan not found.");

        // A disbursement already in progress cannot be started again. A failed
        // Guarantor payment leaves a recoverable disbursement that may be retried.
        var existing = await _paymentRepository.GetByLoanAsync(loan.Id, ct);
        if (existing is not null && existing.Status != DisbursementStatus.Failed)
        {
            throw new InvalidPaymentException("A disbursement for this loan has already been initiated.");
        }

        if (loan.Status != LoanStatus.Approved && loan.Status != LoanStatus.DisbursementPending)
        {
            throw new InvalidPaymentException("Only approved loans can be disbursed.");
        }

        var fund = await _fundRepository.GetByIdAsync(loan.FundId, ct)
            ?? throw new InvalidPaymentException("Fund not found.");

        if (fund.GuarantorId != guarantorId)
        {
            throw new InvalidPaymentException("You do not have permission to disburse this loan.");
        }

        var recipient = await _paymentRepository.GetActiveRecipientForMemberAsync(loan.MemberId, ct);
        if (recipient is null || !recipient.IsActive || string.IsNullOrWhiteSpace(recipient.ProviderRecipientCode))
        {
            throw new InvalidPaymentException("The member does not have a verified bank recipient yet.");
        }

        if (string.IsNullOrWhiteSpace(recipient.ProviderSubaccountCode))
        {
            throw new InvalidPaymentException("The member does not have a settlement subaccount yet. Ask them to re-verify their bank details.");
        }

        var provider = _providerRegistry.Get(recipient.Provider)
            ?? throw new InvalidPaymentException($"Payment provider '{recipient.Provider}' is not available.");

        var disbursedAmount = loan.ApprovedAmount ?? loan.RequestedAmount;

        // The Guarantor pays the gross amount whose net (gross minus provider
        // fee) equals the approved amount, so the collection split settles the
        // member's subaccount exactly the approved amount and the platform
        // retains the fee (ADR-044). Provider charges apply to the gross, so the
        // gross is solved rather than sized with a fee calculated on the
        // approved amount, then rounded up to the nearest whole naira. The
        // provider's fee at confirmation remains authoritative and reconciles
        // the recorded net (AmountNet).
        var gross = decimal.Ceiling(CapitalTransaction.ComputeGrossForNet(disbursedAmount));
        var estimatedFee = gross - disbursedAmount;

        var idempotencyKey = Guid.NewGuid().ToString("N");
        var reference = $"CAP-{loan.Id:N}"[..20];

        var collection = await provider.InitializeCollectionAsync(new CollectionInitiationRequest
        {
            Provider = recipient.Provider,
            Email = payerEmail,
            Amount = gross,
            Reference = reference,
            CallbackUrl = callbackUrl,
            SubaccountCode = recipient.ProviderSubaccountCode,
            TransactionCharge = estimatedFee,
            Bearer = "account",
        }, ct);

        if (!collection.Success || string.IsNullOrWhiteSpace(collection.ProviderReference) || string.IsNullOrWhiteSpace(collection.AuthorizationUrl))
        {
            throw new InvalidPaymentException($"Disbursement payment could not be initiated: {collection.RejectionReason ?? "unknown error"}.");
        }

        // Per-loan capital payment: exactly this loan's Guarantor funding.
        var capital = CapitalTransaction.Create(
            fund.Id,
            loan.Id,
            guarantorId,
            recipient.Provider,
            gross,
            collection.ProviderReference);
        capital.SetAuthorizationUrl(collection.AuthorizationUrl);
        _capitalFundingRepository.Add(capital);

        // The disbursement settlement record. On retry after a failed payment,
        // reuse the existing record with the new collection reference.
        DisbursementTransaction transaction;
        if (existing is not null && existing.Status == DisbursementStatus.Failed)
        {
            existing.ResetForRetry(collection.ProviderReference, idempotencyKey);
            transaction = existing;
        }
        else
        {
            transaction = DisbursementTransaction.Initiate(
                loan.Id,
                recipient.Id,
                disbursedAmount,
                collection.ProviderReference,
                idempotencyKey);
            _paymentRepository.AddDisbursement(transaction);
        }

        if (loan.Status == LoanStatus.Approved)
        {
            loan.MarkDisbursementPending();
        }

        await _auditLog.RecordAsync(guarantorId, "Disbursement.Initiated", "Loan", loan.Id,
            $"Amount={disbursedAmount}, Gross={gross:N2}, Fee={estimatedFee:N2}, ProviderRef={collection.ProviderReference}", ct);
        await _paymentRepository.SaveChangesAsync(ct);
        await _capitalFundingRepository.SaveChangesAsync(ct);

        return ToDisbursementDto(transaction, capital);
    }

    /// <summary>
    /// Processes a provider collection event (charge.success / charge.failed).
    /// Idempotent: duplicate events for the same transaction are ignored. A
    /// success confirms the Guarantor's payment and marks the loan DISBURSED
    /// (the member is settled through their subaccount); a failure is recorded
    /// explicitly and the loan is left in a recoverable DISBURSEMENT_PENDING
    /// state so the Guarantor can retry.
    /// </summary>
    public async Task<DisbursementDto?> ProcessProviderWebhookAsync(
        string provider,
        string eventId,
        string providerReference,
        DisbursementStatus status,
        string? detail = null,
        CancellationToken ct = default)
    {
        // Reject duplicate events before any work.
        if (await _paymentRepository.HasProcessedEventAsync(eventId, ct))
        {
            return null;
        }

        var transaction = await _paymentRepository.GetByProviderReferenceAsync(providerReference, ct)
            ?? throw new InvalidPaymentException("Unknown provider payment reference.");

        var applied = transaction.ApplyProviderEvent(eventId, status, detail);
        if (!applied)
        {
            return null;
        }

        // On success, mark the loan disbursed (only if still pending) and ensure
        // its repayment schedule exists so members can repay immediately.
        if (status == DisbursementStatus.Successful)
        {
            var loan = await _loanRepository.GetByIdAsync(transaction.LoanId, ct);
            if (loan is not null && loan.Status == LoanStatus.DisbursementPending)
            {
                loan.MarkDisbursed();
                await _auditLog.RecordAsync(SystemActorId, "Disbursement.Succeeded", "Loan", loan.Id,
                    $"Amount={transaction.Amount}, ProviderRef={providerReference}", ct);

                await _repaymentService.EnsureScheduleAsync(SystemActorId, loan.Id, ct);
            }
        }
        else
        {
            await _auditLog.RecordAsync(SystemActorId, $"Disbursement.{status}", "DisbursementTransaction", transaction.Id,
                $"ProviderRef={providerReference}, Detail={detail}", ct);
        }

        await _paymentRepository.SaveChangesAsync(ct);
        return ToDisbursementDto(transaction);
    }

    /// <summary>
    /// Finalises the disbursement linked to a Guarantor's collection
    /// (charge.success / charge.failed webhook). This is the production
    /// confirmation path for "pay per disbursement" (ADR-044): a successful
    /// charge marks the loan DISBURSED, a failed charge leaves it in a
    /// recoverable DISBURSEMENT_PENDING state for retry. Returns null when no
    /// disbursement is linked to the reference, so a standalone capital payment
    /// is not treated as an error. Idempotent with the dev-only verify endpoint.
    /// </summary>
    public async Task<DisbursementDto?> FinaliseDisbursementForChargeAsync(
        string eventId,
        string providerReference,
        bool chargePaid,
        string? detail = null,
        CancellationToken ct = default)
    {
        var transaction = await _paymentRepository.GetByProviderReferenceAsync(providerReference, ct);
        if (transaction is null)
        {
            return null;
        }

        return await ProcessProviderWebhookAsync(
            DefaultProvider,
            eventId,
            providerReference,
            chargePaid ? DisbursementStatus.Successful : DisbursementStatus.Failed,
            detail,
            ct);
    }

    public async Task<PaymentRecipientDto?> GetRecipientForMemberAsync(Guid memberId, CancellationToken ct = default)
    {
        var latest = await _paymentRepository.GetLatestRecipientForMemberAsync(memberId, ct);
        return latest is null ? null : ToRecipientDto(latest);
    }

    public async Task<DisbursementDto?> GetDisbursementForLoanAsync(Guid loanId, CancellationToken ct = default)
    {
        var transaction = await _paymentRepository.GetByLoanAsync(loanId, ct);
        if (transaction is null)
        {
            return null;
        }

        var capital = await _capitalFundingRepository.GetByProviderReferenceAsync(transaction.ProviderReference, ct);
        return ToDisbursementDto(transaction, capital);
    }

    private static PaymentRecipientDto ToRecipientDto(PaymentRecipient r) => new()
    {
        Id = r.Id,
        MemberId = r.MemberId,
        Provider = r.Provider,
        BankName = r.BankName,
        BankCode = r.BankCode,
        AccountName = r.AccountName,
        AccountNumberMasked = MaskAccountNumber(r.AccountNumber),
        Status = r.Status,
        IsActive = r.IsActive,
    };

    private static string MaskAccountNumber(string accountNumber)
    {
        var digits = accountNumber.Trim();
        if (digits.Length <= 4)
        {
            return new string('•', digits.Length);
        }

        return "•••• " + digits[^4..];
    }

    private static DisbursementDto ToDisbursementDto(DisbursementTransaction t) => ToDisbursementDto(t, null);

    private static DisbursementDto ToDisbursementDto(DisbursementTransaction t, CapitalTransaction? capital) => new()
    {
        Id = t.Id,
        LoanId = t.LoanId,
        RecipientId = t.RecipientId,
        Amount = t.Amount,
        Currency = t.Currency,
        Status = t.Status,
        ProviderReference = t.ProviderReference,
        FailureReason = t.FailureReason,
        InitiatedAtUtc = t.InitiatedAtUtc,
        CompletedAtUtc = t.CompletedAtUtc,
        AuthorizationUrl = capital?.AuthorizationUrl,
        GrossAmount = capital?.AmountGross,
        EstimatedFee = capital?.ProviderFee,
        CapitalTransactionId = capital?.Id,
    };
}
