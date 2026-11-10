using FamilyTrustFund.Domain.Funds;

namespace FamilyTrustFund.Application.Funds;

/// <summary>
/// Server-calculated representation of a fund returned to clients.
/// </summary>
public sealed class FundDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public FundType Type { get; init; }
    public string JoinCode { get; init; } = string.Empty;
    public decimal CommittedCapital { get; init; }

    /// <summary>
    /// The Fund Originator. For a single-Guarantor fund this is the sole owner;
    /// for a group fund it is the only Guarantor who may initiate disbursement
    /// (ADR-051, ADR-053). Clients use it to gate originator-only actions; the
    /// server enforces the rule regardless.
    /// </summary>
    public Guid GuarantorId { get; init; }

    /// <summary>Single-Guarantor or Group Guarantor operation (ADR-051).</summary>
    public FundGuarantorMode GuarantorMode { get; init; }

    public decimal ContributionMultiplier { get; init; }
    public decimal? InterestRate { get; init; }
    public int MinLoanTermMonths { get; init; }
    public int MaxLoanTermMonths { get; init; }
    public string? HowItWorks { get; init; }
    public string? RepaymentBankName { get; init; }
    public string? RepaymentAccountNumber { get; init; }
    public string? RepaymentAccountName { get; init; }
    public FundStatus Status { get; init; }
    public DateTime? TransitionedAtUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
