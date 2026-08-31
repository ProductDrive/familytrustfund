using FamilyTrustFund.Domain.Loans;

namespace FamilyTrustFund.Application.Loans;

/// <summary>
/// Member's loan request.
/// </summary>
public sealed class RequestLoanRequest
{
    /// <summary>Target fund to borrow from.</summary>
    public Guid FundId { get; init; }

    /// <summary>Requested loan amount in NGN.</summary>
    public decimal Amount { get; init; }

    /// <summary>How often the member wants to repay.</summary>
    public RepaymentFrequency Frequency { get; init; }

    /// <summary>Optional reason or purpose for the loan.</summary>
    public string? Purpose { get; init; }
}

/// <summary>
/// Guarantor's decision on a loan request.
/// </summary>
public sealed class ApproveLoanRequest
{
    /// <summary>Loan ID to approve.</summary>
    public Guid LoanId { get; init; }

    /// <summary>
    /// Approved amount. May differ from the member's requested amount.
    /// </summary>
    public decimal ApprovedAmount { get; init; }

    /// <summary>
    /// Repayment frequency chosen by the Guarantor. May differ from the
    /// member's requested frequency (ADR-010).
    /// </summary>
    public RepaymentFrequency ApprovedFrequency { get; init; }
}

/// <summary>
/// Guarantor's rejection of a loan request.
/// </summary>
public sealed class RejectLoanRequest
{
    /// <summary>Loan ID to reject.</summary>
    public Guid LoanId { get; init; }

    /// <summary>Optional rejection reason shown to the member.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Server-calculated representation of a loan returned to clients.
/// </summary>
public sealed class LoanDto
{
    public Guid Id { get; init; }
    public Guid FundId { get; init; }
    public string FundName { get; init; } = string.Empty;
    public Guid MemberId { get; init; }
    public string MemberDisplayName { get; init; } = string.Empty;
    public string MemberEmail { get; init; } = string.Empty;
    public decimal RequestedAmount { get; init; }
    public decimal? ApprovedAmount { get; init; }
    public decimal InterestRate { get; init; }
    public RepaymentFrequency RequestedFrequency { get; init; }
    public RepaymentFrequency? ApprovedFrequency { get; init; }
    public LoanStatus Status { get; init; }
    public LoanFundingSource FundingSource { get; init; }
    public decimal OutstandingBalance { get; init; }
    public decimal TotalRepayable { get; init; }
    public string? Purpose { get; init; }
    public string? RejectionReason { get; init; }
    public DateTime RequestedAtUtc { get; init; }
    public DateTime? ApprovedAtUtc { get; init; }
    public DateTime? RejectedAtUtc { get; init; }
}

/// <summary>
/// Summary of the fund's lending state, returned alongside loan operations
/// so the client can display server-authoritative financial context.
/// </summary>
public sealed class LendingCapacityDto
{
    public Guid FundId { get; init; }
    public decimal CommittedCapital { get; init; }
    public decimal ActiveDisbursedLoans { get; init; }
    public decimal AvailableLendingCapacity { get; init; }

    /// <summary>Family only: the member's Fund Credit × multiplier.</summary>
    public decimal? FamilyBorrowingEntitlement { get; init; }

    /// <summary>Family only: the member's current Fund Credit.</summary>
    public decimal? FundCredit { get; init; }
}
