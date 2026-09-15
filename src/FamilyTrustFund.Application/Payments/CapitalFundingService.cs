using FamilyTrustFund.Application.Audit;
using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Payments;

namespace FamilyTrustFund.Application.Payments;

/// <summary>
/// Application service for Guarantor capital payments (funded capital). The
/// Guarantor does not pre-fund the platform: they pay per disbursement, at the
/// moment a loan is paid out. Each payment is tied to exactly one loan
/// (ADR-044). Provider verification/webhooks are authoritative and all
/// financial transitions are idempotent (ADR-026, ADR-044).
/// </summary>
public class CapitalFundingService
{
    public const string DefaultProvider = "Paystack";

    private readonly IFundRepository _fundRepository;
    private readonly ICapitalFundingRepository _capitalFundingRepository;
    private readonly IPaymentProviderRegistry _providerRegistry;
    private readonly IAuditLog _auditLog;

    public CapitalFundingService(
        IFundRepository fundRepository,
        ICapitalFundingRepository capitalFundingRepository,
        IPaymentProviderRegistry providerRegistry,
        IAuditLog auditLog)
    {
        _fundRepository = fundRepository;
        _capitalFundingRepository = capitalFundingRepository;
        _providerRegistry = providerRegistry;
        _auditLog = auditLog;
    }

    /// <summary>
    /// Returns the gross amount (target + estimated fee) a Guarantor must pay so
    /// that <paramref name="amount"/> becomes funded capital. Fault-tolerant:
    /// if the provider estimate is unavailable, a conservative local estimate is
    /// used. The provider's fee at confirmation is always authoritative.
    /// </summary>
    public async Task<CapitalFundingEstimateDto> EstimateAsync(
        Guid guarantorId,
        Guid fundId,
        CapitalFundingEstimateRequest request,
        CancellationToken ct = default)
    {
        await EnsureOwnedFundAsync(guarantorId, fundId, ct);
        if (request.Amount <= 0)
        {
            throw new InvalidPaymentException("Funding amount must be greater than zero.");
        }

        var provider = _providerRegistry.Get(DefaultProvider)
            ?? throw new InvalidPaymentException($"Payment provider '{DefaultProvider}' is not available.");

        var result = await provider.EstimateCollectionChargeAsync(request.Amount, "NGN", ct);
        if (result.Success)
        {
            return new CapitalFundingEstimateDto
            {
                Amount = request.Amount,
                EstimatedFee = result.EstimatedFee,
                GrossAmount = result.GrossAmount,
            };
        }

        var fallbackFee = CapitalTransaction.ComputeEstimatedFee(request.Amount);
        return new CapitalFundingEstimateDto
        {
            Amount = request.Amount,
            EstimatedFee = fallbackFee,
            GrossAmount = request.Amount + fallbackFee,
        };
    }

    /// <summary>
    /// Verifies a pending capital payment with the provider and confirms it
    /// (recording the authoritative fee and net). Idempotent: verifying an
    /// already-finalised payment returns the stored state.
    /// </summary>
    public async Task<CapitalTransactionDto> VerifyAsync(
        Guid guarantorId,
        Guid fundId,
        string providerReference,
        CancellationToken ct = default)
    {
        var transaction = await GetOwnedTransactionAsync(guarantorId, fundId, providerReference, ct);

        if (transaction.Status == CapitalTransactionStatus.Confirmed
            || transaction.Status == CapitalTransactionStatus.Failed)
        {
            return ToDto(transaction);
        }

        var provider = _providerRegistry.Get(transaction.Provider)
            ?? throw new InvalidPaymentException($"Payment provider '{transaction.Provider}' is not available.");

        var result = await provider.VerifyCollectionAsync(transaction.ProviderReference, ct);

        if (result.Paid)
        {
            var applied = transaction.ApplyProviderEvent(
                $"verify-{transaction.ProviderReference}", true, result.ProviderFee);
            if (applied)
            {
                await _auditLog.RecordAsync(guarantorId, "CapitalPayment.Confirmed", "CapitalTransaction", transaction.Id,
                    $"Gross={transaction.AmountGross:N2}, Fee={result.ProviderFee:N2}, Net={transaction.AmountNet:N2}, ProviderRef={transaction.ProviderReference}", ct);
                await _capitalFundingRepository.SaveChangesAsync(ct);
            }
        }

        return ToDto(transaction);
    }

    /// <summary>
    /// Processes a provider collection webhook (charge.success / charge.failed).
    /// Idempotent: a duplicate event for the same transaction is ignored.
    /// </summary>
    public async Task<CapitalTransactionDto?> ProcessChargeWebhookAsync(
        string provider,
        string eventId,
        string providerReference,
        bool paid,
        decimal? providerFee,
        string? detail = null,
        CancellationToken ct = default)
    {
        var transaction = await _capitalFundingRepository.GetByProviderReferenceAsync(providerReference, ct)
            ?? throw new InvalidPaymentException("Unknown provider collection reference.");

        var applied = transaction.ApplyProviderEvent(eventId, paid, providerFee, detail);
        if (!applied)
        {
            return null;
        }

        var action = paid ? "CapitalPayment.Confirmed" : $"CapitalPayment.{transaction.Status}";
        var auditDetail = paid
            ? $"ProviderRef={providerReference}, Fee={providerFee}, Net={transaction.AmountNet:N2}"
            : $"ProviderRef={providerReference}, Detail={detail}";
        await _auditLog.RecordAsync(DisbursementService.SystemActorId, action, "CapitalTransaction", transaction.Id,
            auditDetail, ct);
        await _capitalFundingRepository.SaveChangesAsync(ct);

        return ToDto(transaction);
    }

    /// <summary>
    /// Server-authoritative summary of a fund's per-loan Guarantor payments.
    /// There is no pre-funded pool; Guarantors pay at disbursement, so this
    /// reports confirmed net payments to date (ADR-044).
    /// </summary>
    public async Task<CapitalFundingSummaryDto> GetSummaryAsync(
        Guid guarantorId,
        Guid fundId,
        CancellationToken ct = default)
    {
        await EnsureOwnedFundAsync(guarantorId, fundId, ct);

        var totalFunded = await _capitalFundingRepository.SumConfirmedNetByFundAsync(fundId, ct);
        var count = await _capitalFundingRepository.CountByFundAsync(fundId, ct);

        return new CapitalFundingSummaryDto
        {
            TotalFunded = totalFunded,
            TransactionCount = count,
        };
    }

    public async Task<CapitalTransactionPageDto> GetTransactionsAsync(
        Guid guarantorId,
        Guid fundId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        await EnsureOwnedFundAsync(guarantorId, fundId, ct);

        if (page < 1)
        {
            page = 1;
        }

        if (pageSize < 1)
        {
            pageSize = 10;
        }

        var items = await _capitalFundingRepository.GetByFundAsync(fundId, page, pageSize, ct);
        var total = await _capitalFundingRepository.CountByFundAsync(fundId, ct);

        return new CapitalTransactionPageDto
        {
            Items = items.Select(ToDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    private async Task<Fund> EnsureOwnedFundAsync(Guid guarantorId, Guid fundId, CancellationToken ct)
    {
        var fund = await _fundRepository.GetByIdAsync(fundId, ct)
            ?? throw new InvalidPaymentException("Fund not found.");

        if (fund.GuarantorId != guarantorId)
        {
            throw new InvalidPaymentException("You do not have permission to fund this fund.");
        }

        return fund;
    }

    private async Task<CapitalTransaction> GetOwnedTransactionAsync(
        Guid guarantorId,
        Guid fundId,
        string providerReference,
        CancellationToken ct)
    {
        var transaction = await _capitalFundingRepository.GetByProviderReferenceAsync(providerReference, ct)
            ?? throw new InvalidPaymentException("Capital payment not found.");

        if (transaction.FundId != fundId)
        {
            throw new InvalidPaymentException("You do not have permission to access this capital payment.");
        }

        var fund = await _fundRepository.GetByIdAsync(fundId, ct)
            ?? throw new InvalidPaymentException("Fund not found.");

        if (fund.GuarantorId != guarantorId)
        {
            throw new InvalidPaymentException("You do not have permission to access this capital payment.");
        }

        return transaction;
    }

    private static CapitalTransactionDto ToDto(CapitalTransaction t) => new()
    {
        Id = t.Id,
        FundId = t.FundId,
        LoanId = t.LoanId,
        Provider = t.Provider,
        Status = t.Status,
        AmountGross = t.AmountGross,
        ProviderFee = t.ProviderFee,
        AmountNet = t.AmountNet,
        ProviderReference = t.ProviderReference,
        FailureReason = t.FailureReason,
        InitiatedAtUtc = t.InitiatedAtUtc,
        CompletedAtUtc = t.CompletedAtUtc,
    };
}