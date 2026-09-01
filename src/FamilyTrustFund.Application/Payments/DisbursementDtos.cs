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
/// Server-authoritative disbursement representation.
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
}
