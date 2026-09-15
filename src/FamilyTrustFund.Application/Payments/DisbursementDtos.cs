using FamilyTrustFund.Domain.Payments;

namespace FamilyTrustFund.Application.Payments;

/// <summary>
/// Member supplies/updates their bank details for loan disbursement.
/// </summary>
public sealed class SaveRecipientRequest
{
    public string BankCode { get; init; } = string.Empty;
    public string BankName { get; init; } = string.Empty;
    public string AccountNumber { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
}

/// <summary>
/// Guarantor initiates disbursement of an approved loan.
/// </summary>
public sealed class InitiateDisbursementRequest
{
    public Guid LoanId { get; init; }

    /// <summary>
    /// Where the provider should return the Guarantor after checkout. The
    /// provider identifies the transaction via the <c>reference</c> query
    /// parameter it appends. Only http(s) URLs are accepted.
    /// </summary>
    public string? CallbackUrl { get; init; }
}

/// <summary>
/// Public recipient representation. Sensitive account numbers are masked.
/// </summary>
public sealed class PaymentRecipientDto
{
    public Guid Id { get; init; }
    public Guid MemberId { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string BankName { get; init; } = string.Empty;
    public string BankCode { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;

    /// <summary>Masked account number, e.g. "•••• 1234". Never full.</summary>
    public string AccountNumberMasked { get; init; } = string.Empty;

    public PaymentRecipientStatus Status { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>
/// Server-authoritative disbursement representation. When the loan is awaiting
/// the Guarantor's payment, the payout-related fields (<c>AuthorizationUrl</c>,
/// <c>GrossAmount</c>, <c>EstimatedFee</c>, <c>CapitalTransactionId</c>) are
/// populated; otherwise they are null.
/// </summary>
public sealed class DisbursementDto
{
    public Guid Id { get; init; }
    public Guid LoanId { get; init; }
    public Guid RecipientId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "NGN";
    public DisbursementStatus Status { get; init; }
    public string ProviderReference { get; init; } = string.Empty;
    public string? FailureReason { get; init; }
    public DateTime InitiatedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }

    /// <summary>Provider checkout URL the Guarantor must complete to pay for this disbursement.</summary>
    public string? AuthorizationUrl { get; init; }

    /// <summary>Gross amount the Guarantor pays (approved amount + fee).</summary>
    public decimal? GrossAmount { get; init; }

    /// <summary>Estimated provider fee for display; the confirmed fee is authoritative.</summary>
    public decimal? EstimatedFee { get; init; }

    /// <summary>The per-loan capital payment funding this disbursement.</summary>
    public Guid? CapitalTransactionId { get; init; }
}
