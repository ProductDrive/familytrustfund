using FamilyTrustFund.Domain.Payments;

namespace FamilyTrustFund.Application.Payments;

/// <summary>
/// Guarantor asks for a charge estimate to fund <see cref="Amount"/> net.
/// </summary>
public sealed class CapitalFundingEstimateRequest
{
    public decimal Amount { get; init; }
}

/// <summary>
/// Server-calculated funding estimate. The Guarantor pays gross; the fee is
/// absorbed by the Guarantor (ADR-044).
/// </summary>
public sealed class CapitalFundingEstimateDto
{
    public decimal Amount { get; init; }
    public decimal EstimatedFee { get; init; }
    public decimal GrossAmount { get; init; }
}

/// <summary>
/// Guarantor starts a capital payment for a fund.
/// </summary>
public sealed class InitiateCapitalPaymentRequest
{
    public decimal Amount { get; init; }

    /// <summary>
    /// Where the provider should return the Guarantor after checkout. The
    /// provider identifies the transaction via the <c>reference</c> query
    /// parameter it appends. Only http(s) URLs are accepted.
    /// </summary>
    public string? CallbackUrl { get; init; }
}

/// <summary>
/// Result of initiating a capital payment, including the provider checkout URL
/// the Guarantor is redirected to.
/// </summary>
public sealed class CapitalPaymentInitiationDto
{
    public Guid TransactionId { get; init; }
    public string ProviderReference { get; init; } = string.Empty;

    /// <summary>Provider-hosted checkout URL (authorization_url).</summary>
    public string AuthorizationUrl { get; init; } = string.Empty;
}

/// <summary>
/// Public representation of a per-loan capital payment. Money-related amounts
/// are server-calculated and authoritative.
/// </summary>
public sealed class CapitalTransactionDto
{
    public Guid Id { get; init; }
    public Guid FundId { get; init; }
    public Guid LoanId { get; init; }
    public string Provider { get; init; } = string.Empty;
    public CapitalTransactionStatus Status { get; init; }
    public decimal AmountGross { get; init; }
    public decimal? ProviderFee { get; init; }
    public decimal? AmountNet { get; init; }
    public string ProviderReference { get; init; } = string.Empty;
    public string? FailureReason { get; init; }
    public DateTime InitiatedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
}

/// <summary>
/// Server-authoritative summary of a fund's per-loan Guarantor payments. There
/// is no "funded pool": Guarantors pay at disbursement, so this reports what
/// has been paid and how many loans it funded.
/// </summary>
public sealed class CapitalFundingSummaryDto
{
    /// <summary>Sum of confirmed net capital payments for the fund.</summary>
    public decimal TotalFunded { get; init; }

    /// <summary>Total capital payment transactions recorded for the fund.</summary>
    public int TransactionCount { get; init; }
}

/// <summary>
/// Paged list of capital payments for a fund.
/// </summary>
public sealed class CapitalTransactionPageDto
{
    public IReadOnlyList<CapitalTransactionDto> Items { get; init; } = Array.Empty<CapitalTransactionDto>();
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}