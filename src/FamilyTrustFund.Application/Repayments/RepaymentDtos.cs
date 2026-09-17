using FamilyTrustFund.Domain.Repayments;

namespace FamilyTrustFund.Application.Repayments;

/// <summary>A single instalment returned to clients.</summary>
public sealed class ScheduleItemDto
{
    public Guid Id { get; init; }
    public int Sequence { get; init; }
    public DateTime DueDateUtc { get; init; }
    public decimal PrincipalDue { get; init; }
    public decimal InterestDue { get; init; }
    public decimal ExpectedAmount { get; init; }
    public decimal PaidAmount { get; init; }
    public DateTime? PaidAtUtc { get; init; }
    public ScheduleItemStatus Status { get; init; }
}

/// <summary>A repayment schedule revision returned to clients.</summary>
public sealed class ScheduleDto
{
    public Guid Id { get; init; }
    public Guid LoanId { get; init; }
    public int Version { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public bool IsCurrent { get; init; }
    public IReadOnlyList<ScheduleItemDto> Items { get; init; } = Array.Empty<ScheduleItemDto>();
}

/// <summary>Request to record a payment against a loan.</summary>
public sealed class MakeRepaymentRequest
{
    public Guid LoanId { get; init; }
    public decimal Amount { get; init; }
    public string? Note { get; init; }
}

/// <summary>A recorded repayment returned to clients.</summary>
public sealed class RepaymentDto
{
    public Guid Id { get; init; }
    public Guid LoanId { get; init; }
    public string FundName { get; init; } = string.Empty;
    public int ScheduleVersion { get; init; }
    public RepaymentKind Kind { get; init; }
    public decimal ExpectedAmount { get; init; }
    public decimal ActualAmount { get; init; }
    public decimal Surplus { get; init; }
    public DateTime PaidAtUtc { get; init; }
    public string? Note { get; init; }
}

/// <summary>A recorded repayment with its fund context, used internally
/// when projecting cross-fund paginated history from the repository.</summary>
public sealed class RepaymentWithFund
{
    public Repayment Repayment { get; init; } = default!;
    public string FundName { get; init; } = string.Empty;
}

/// <summary>Paginated page of repayment records returned to clients.</summary>
public sealed class PagedRepaymentsResult
{
    public IReadOnlyList<RepaymentDto> Items { get; init; } = Array.Empty<RepaymentDto>();
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

/// <summary>Server-calculated summary of a loan's repayment position.</summary>
public sealed class RepaymentSummaryDto
{
    /// <summary>
    /// Total amount still owed: outstanding principal + unpaid interest
    /// (a Family loan has no interest, so this equals its principal).
    /// </summary>
    public decimal OutstandingBalance { get; init; }

    /// <summary>Unpaid interest remaining on the loan (0 for Family loans).</summary>
    public decimal OutstandingInterest { get; init; }

    public decimal TotalExpected { get; init; }
    public decimal TotalPaid { get; init; }
    public decimal TotalSurplus { get; init; }
    public int OverdueItems { get; init; }
    public decimal OverdueAmount { get; init; }

    /// <summary>Server-calculated amount required to settle the loan now
    /// (the outstanding obligation, principal + interest).</summary>
    public decimal SettlementQuote { get; init; }
}

/// <summary>A manually declared repayment awaiting Guarantor confirmation.</summary>
public sealed class ReportPendingRepaymentRequest
{
    public Guid LoanId { get; init; }
    public decimal Amount { get; init; }
    public RepaymentKind Kind { get; init; }
    public string? Reference { get; init; }
    public string? Note { get; init; }
}

/// <summary>Guarantor instruction to confirm a pending repayment.</summary>
public sealed class ConfirmPendingRepaymentRequest
{
    public Guid PendingRepaymentId { get; init; }

    /// <summary>Optional free-text note added by the Guarantor on confirmation.</summary>
    public string? Note { get; init; }
}

/// <summary>Guarantor instruction to reject a pending repayment.</summary>
public sealed class RejectPendingRepaymentRequest
{
    public Guid PendingRepaymentId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Result returned after confirming a pending repayment.</summary>
public sealed class ConfirmPendingRepaymentResult
{
    public PendingRepaymentDto Pending { get; init; } = new();
    public bool RepaymentPosted { get; init; }
}

/// <summary>A pending repayment returned to clients.</summary>
public sealed class PendingRepaymentDto
{
    public Guid Id { get; init; }
    public Guid LoanId { get; init; }
    public Guid MemberId { get; init; }
    public Guid FundId { get; init; }
    public string FundName { get; init; } = string.Empty;
    public string MemberDisplayName { get; init; } = string.Empty;
    public string MemberEmail { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public RepaymentKind Kind { get; init; }
    public string? Reference { get; init; }
    public string? Note { get; init; }
    public PendingRepaymentStatus Status { get; init; }
    public string? RejectionReason { get; init; }
    public string? ConfirmationNote { get; init; }
    public DateTime ReportedAtUtc { get; init; }
    public DateTime? ConfirmedAtUtc { get; init; }
    public DateTime? RejectedAtUtc { get; init; }

    /// <summary>Whether the member uploaded payment evidence for this repayment.</summary>
    public bool HasEvidence { get; set; }
}
