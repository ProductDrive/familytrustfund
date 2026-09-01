using FamilyTrustFund.Application.Audit;
using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Application.Loans;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Payments;

namespace FamilyTrustFund.Application.Payments;

/// <summary>
/// Application service for loan disbursement: bank-recipient management,
/// transfer initiation and idempotent webhook-driven finalisation.
/// </summary>
/// <remarks>
/// A successful provider API request alone does not mark a loan disbursed.
/// Only a provider webhook confirmation changes the loan to DISBURSED and
/// records actual disbursed capital (AGENTS §3). All financial transitions are
/// transactional and idempotent.
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
    private readonly ILoanRepository _loanRepository;
    private readonly IFundRepository _fundRepository;
    private readonly IAuditLog _auditLog;

    public DisbursementService(
        IPaymentRepository paymentRepository,
        IPaymentProviderRegistry providerRegistry,
        ILoanRepository loanRepository,
        IFundRepository fundRepository,
        IAuditLog auditLog)
    {
        _paymentRepository = paymentRepository;
        _providerRegistry = providerRegistry;
        _loanRepository = loanRepository;
        _fundRepository = fundRepository;
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
        await _auditLog.RecordAsync(memberId, "PaymentRecipient.Verified", "PaymentRecipient", latest.Id,
            "Recipient verified with provider", ct);
        await _paymentRepository.SaveChangesAsync(ct);

        return ToRecipientDto(latest);
    }

    /// <summary>
    /// Guarantor initiates disbursement of an approved loan to the member's
    /// active recipient. Creates a Pending disbursement transaction and moves
    /// the loan to DISBURSEMENT_PENDING. Final status is established by webhook.
    /// </summary>
    public async Task<DisbursementDto> InitiateDisbursementAsync(
        Guid guarantorId,
        InitiateDisbursementRequest request,
        CancellationToken ct = default)
    {
        var loan = await _loanRepository.GetByIdAsync(request.LoanId, ct)
            ?? throw new InvalidPaymentException("Loan not found.");

        // Prevent double initiation regardless of the application's state.
        var existing = await _paymentRepository.GetByLoanAsync(loan.Id, ct);
        if (existing is not null)
        {
            throw new InvalidPaymentException("A disbursement for this loan has already been initiated.");
        }

        if (loan.Status != LoanStatus.Approved)
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

        var provider = _providerRegistry.Get(recipient.Provider)
            ?? throw new InvalidPaymentException($"Payment provider '{recipient.Provider}' is not available.");

        var disbursedAmount = loan.ApprovedAmount ?? loan.RequestedAmount;
        var idempotencyKey = Guid.NewGuid().ToString("N");
        var reference = $"FTF-{loan.Id:N}"[..20];

        var result = await provider.InitiateTransferAsync(new InitiateTransferRequest
        {
            Provider = recipient.Provider,
            ProviderRecipientCode = recipient.ProviderRecipientCode,
            Amount = disbursedAmount,
            Reference = reference,
            IdempotencyKey = idempotencyKey,
        }, ct);

        if (!result.Success || string.IsNullOrWhiteSpace(result.ProviderReference))
        {
            throw new InvalidPaymentException($"Disbursement could not be initiated: {result.RejectionReason ?? "unknown error"}.");
        }

        var transaction = DisbursementTransaction.Initiate(
            loan.Id,
            recipient.Id,
            disbursedAmount,
            result.ProviderReference,
            idempotencyKey);

        _paymentRepository.AddDisbursement(transaction);
        loan.MarkDisbursementPending();

        await _auditLog.RecordAsync(guarantorId, "Disbursement.Initiated", "Loan", loan.Id,
            $"Amount={disbursedAmount}, ProviderRef={result.ProviderReference}", ct);
        await _paymentRepository.SaveChangesAsync(ct);

        return ToDisbursementDto(transaction);
    }

    /// <summary>
    /// Processes a provider webhook event. Idempotent: duplicate events for the
    /// same transaction are ignored. A success marks the loan DISBURSED; a
    /// failure/reversal is recorded explicitly and the loan is left in a
    /// recoverable state.
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
            ?? throw new InvalidPaymentException("Unknown provider transfer reference.");

        var applied = transaction.ApplyProviderEvent(eventId, status, detail);
        if (!applied)
        {
            return null;
        }

        // On success, mark the loan disbursed (only if still pending).
        if (status == DisbursementStatus.Successful)
        {
            var loan = await _loanRepository.GetByIdAsync(transaction.LoanId, ct);
            if (loan is not null && loan.Status == LoanStatus.DisbursementPending)
            {
                loan.MarkDisbursed();
                await _auditLog.RecordAsync(SystemActorId, "Disbursement.Succeeded", "Loan", loan.Id,
                    $"Amount={transaction.Amount}, ProviderRef={providerReference}", ct);
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

    public async Task<PaymentRecipientDto?> GetRecipientForMemberAsync(Guid memberId, CancellationToken ct = default)
    {
        var active = await _paymentRepository.GetActiveRecipientForMemberAsync(memberId, ct);
        return active is null ? null : ToRecipientDto(active);
    }

    public async Task<DisbursementDto?> GetDisbursementForLoanAsync(Guid loanId, CancellationToken ct = default)
    {
        var transaction = await _paymentRepository.GetByLoanAsync(loanId, ct);
        return transaction is null ? null : ToDisbursementDto(transaction);
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

    private static DisbursementDto ToDisbursementDto(DisbursementTransaction t) => new()
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
    };
}
