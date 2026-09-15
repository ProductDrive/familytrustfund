using FamilyTrustFund.Domain.Contributions;

namespace FamilyTrustFund.Application.Contributions;

/// <summary>
/// Member reports a contribution to a Family fund.
/// </summary>
public sealed class ReportContributionRequest
{
    /// <summary>Target Family fund to contribute to.</summary>
    public Guid FundId { get; init; }

    /// <summary>Contribution amount in NGN.</summary>
    public decimal Amount { get; init; }

    /// <summary>Optional reference supplied by the member.</summary>
    public string? Reference { get; init; }

    /// <summary>Optional free-text note.</summary>
    public string? Note { get; init; }
}

/// <summary>
/// Guarantor's decision on a pending contribution.
/// </summary>
public sealed class ConfirmContributionRequest
{
    public Guid ContributionId { get; init; }

    /// <summary>Optional free-text note added by the Guarantor on confirmation.</summary>
    public string? Note { get; init; }
}

/// <summary>
/// Guarantor's rejection of a pending contribution.
/// </summary>
public sealed class RejectContributionRequest
{
    public Guid ContributionId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Server-computed Fund Credit summary for a member within a fund.
/// </summary>
public sealed class ContributionSummaryDto
{
    public Guid FundId { get; init; }
    public Guid MemberId { get; init; }
    public decimal FundCredit { get; init; }
    public decimal? BorrowingEntitlement { get; init; }
    public bool HasActiveDisbursedLoan { get; init; }
}

/// <summary>
/// Server-calculated representation of a contribution returned to clients.
/// </summary>
public sealed class ContributionDto
{
    public Guid Id { get; init; }
    public Guid FundId { get; init; }
    public string FundName { get; init; } = string.Empty;
    public Guid MemberId { get; init; }
    public string MemberDisplayName { get; init; } = string.Empty;
    public string MemberEmail { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public ContributionStatus Status { get; init; }
    public string? Reference { get; init; }
    public string? Note { get; init; }
    public string? RejectionReason { get; init; }
    public string? ConfirmationNote { get; init; }
    public DateTime ReportedAtUtc { get; init; }
    public DateTime? ConfirmedAtUtc { get; init; }
    public DateTime? RejectedAtUtc { get; init; }

    /// <summary>Whether the member uploaded payment evidence for this contribution.</summary>
    public bool HasEvidence { get; set; }
}
