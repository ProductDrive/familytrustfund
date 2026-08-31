using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Membership;

namespace FamilyTrustFund.Application.Membership;

/// <summary>
/// A member's own view of a fund they belong to (server-authoritative).
/// </summary>
public sealed class MembershipDto
{
    public Guid FundId { get; init; }
    public string FundName { get; init; } = string.Empty;
    public FundType FundType { get; init; }
    public FundStatus FundStatus { get; init; }
    public string JoinCode { get; init; } = string.Empty;
    public decimal ContributionMultiplier { get; init; }
    public decimal? InterestRate { get; init; }
    public string? HowItWorks { get; init; }
    public MemberStatus Status { get; init; }
    public DateTime JoinedAtUtc { get; init; }
}

/// <summary>
/// Member summary shown to the fund's Guarantor.
/// </summary>
public sealed class FundMemberItem
{
    public Guid MemberId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public MemberStatus Status { get; init; }
    public DateTime JoinedAtUtc { get; init; }
}

/// <summary>
/// Membership summary shown to a Super Admin, including owning fund.
/// </summary>
public sealed class AdminMembershipItem
{
    public Guid MembershipId { get; init; }
    public Guid FundId { get; init; }
    public string FundName { get; init; } = string.Empty;
    public Guid MemberId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public MemberStatus Status { get; init; }
    public DateTime JoinedAtUtc { get; init; }
}