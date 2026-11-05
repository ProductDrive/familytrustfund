using FamilyTrustFund.Domain.Funds;
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

    /// <summary>Requested loan term in months.</summary>
    public int TermMonths { get; init; }

    /// <summary>Requested number of instalments.</summary>
    public int Instalments { get; init; }

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

    /// <summary>Approved loan term in months. May differ from the request.</summary>
    public int ApprovedTermMonths { get; init; }

    /// <summary>Approved number of instalments. May differ from the request.</summary>
    public int ApprovedInstalments { get; init; }

    /// <summary>
    /// True when the Guarantor has confirmed any changed terms with the
    /// borrower. Required by the server whenever approved terms differ from the
    /// member's request.
    /// </summary>
    public bool ConfirmedWithMember { get; init; }
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
/// Cancellation of a loan that has not been disbursed.
/// Members cancel their own pending requests; Guarantors cancel pending or
/// approved loans in their funds. Disbursed/completed loans never cancel.
/// </summary>
public sealed class CancelLoanRequest
{
    /// <summary>Loan ID to cancel.</summary>
    public Guid LoanId { get; init; }
}

/// <summary>
/// Server-calculated representation of a loan returned to clients.
/// </summary>
public sealed class LoanDto
{
    public Guid Id { get; init; }
    public Guid FundId { get; init; }
    public string FundName { get; init; } = string.Empty;
    public FundType FundType { get; init; }
    public Guid MemberId { get; init; }
    public string MemberDisplayName { get; init; } = string.Empty;
    public string MemberEmail { get; init; } = string.Empty;
    public decimal RequestedAmount { get; init; }
    public decimal? ApprovedAmount { get; init; }
    public decimal InterestRate { get; init; }
    public RepaymentFrequency RequestedFrequency { get; init; }
    public RepaymentFrequency? ApprovedFrequency { get; init; }
    public int? RequestedTermMonths { get; init; }
    public int? RequestedInstalments { get; init; }
    public int? ApprovedTermMonths { get; init; }
    public int RepaymentTerm { get; init; }
    public LoanStatus Status { get; init; }
    public LoanFundingSource FundingSource { get; init; }
    public decimal OutstandingBalance { get; init; }
    public decimal TotalRepayable { get; init; }
    public string? Purpose { get; init; }
    public string? RejectionReason { get; init; }
    public DateTime RequestedAtUtc { get; init; }
    public DateTime? ApprovedAtUtc { get; init; }
    public DateTime? RejectedAtUtc { get; init; }
    public DateTime? CancelledAtUtc { get; init; }
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

/// <summary>
/// A member's proposed loan terms, used to preview the repayment breakdown
/// without creating a loan.
/// </summary>
public sealed class LoanPreviewRequest
{
    public Guid FundId { get; init; }
    public decimal Amount { get; init; }
    public RepaymentFrequency Frequency { get; init; }
    public int TermMonths { get; init; }
    public int Instalments { get; init; }
}

/// <summary>
/// Server-calculated preview of a loan's repayment breakdown. The client must
/// render these values, never recalculate them (AGENTS §7, §18.13).
/// </summary>
public sealed class LoanPreviewDto
{
    public Guid FundId { get; init; }
    public FundType FundType { get; init; }
    public decimal Amount { get; init; }
    public RepaymentFrequency Frequency { get; init; }
    public int TermMonths { get; init; }
    public int Instalments { get; init; }
    public decimal InterestRate { get; init; }
    public decimal TotalRepayable { get; init; }
    public decimal InterestAmount { get; init; }

    /// <summary>Typical instalment amount (the first instalment).</summary>
    public decimal InstalmentAmount { get; init; }

    /// <summary>Final instalment amount after rounding.</summary>
    public decimal FinalInstalmentAmount { get; init; }

    public DateTime? FirstDueDateUtc { get; init; }
    public DateTime? FinalDueDateUtc { get; init; }

    public decimal CommittedCapital { get; init; }
    public decimal ActiveDisbursedLoans { get; init; }
    public decimal AvailableLendingCapacity { get; init; }
    public decimal? FamilyBorrowingEntitlement { get; init; }
    public decimal? FundCredit { get; init; }

    public int MinTermMonths { get; init; }
    public int MaxTermMonths { get; init; }

    /// <summary>False when the proposed terms cannot be submitted.</summary>
    public bool IsValid { get; init; }

    /// <summary>Human-readable validation errors, safe to display.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

/// <summary>
/// A Guarantor's proposed approval terms, previewed against the member's
/// original request.
/// </summary>
public sealed class GuarantorLoanPreviewRequest
{
    public Guid LoanId { get; init; }
    public decimal ApprovedAmount { get; init; }
    public RepaymentFrequency ApprovedFrequency { get; init; }
    public int ApprovedTermMonths { get; init; }
    public int ApprovedInstalments { get; init; }
}

/// <summary>
/// A single changed term, with server-formatted values for display.
/// </summary>
public sealed class LoanTermChangeDto
{
    public string Field { get; init; } = string.Empty;
    public string Requested { get; init; } = string.Empty;
    public string Approved { get; init; } = string.Empty;
}

/// <summary>
/// Guarantor approval preview: the repayment breakdown plus the list of terms
/// that differ from the member's request.
/// </summary>
public sealed class GuarantorLoanPreviewDto
{
    public LoanPreviewDto Preview { get; init; } = new();
    public bool TermsChanged { get; init; }
    public IReadOnlyList<LoanTermChangeDto> Changes { get; init; } = Array.Empty<LoanTermChangeDto>();
}
