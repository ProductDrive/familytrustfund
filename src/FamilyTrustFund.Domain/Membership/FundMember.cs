namespace FamilyTrustFund.Domain.Membership;

/// <summary>
/// A member's relationship with a fund.
/// </summary>
/// <remarks>
/// Membership is the bridge between a user and the fund the Guarantor manages.
/// A user can be a member of many funds, and a member belongs to at most one
/// membership per fund (enforced by a unique fund/member key).
/// Membership is separate from the fund itself; borrowing entitlement is
/// derived elsewhere from Fund Credit and fund configuration.
/// </remarks>
public class FundMember
{
    public Guid Id { get; private set; }
    public Guid FundId { get; private set; }
    public Guid MemberId { get; private set; }
    public MemberStatus Status { get; private set; } = MemberStatus.Active;
    public DateTime JoinedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    protected FundMember() { }

    /// <summary>
    /// Creates a new active membership. Joining does not require approval:
    /// the join code itself is the Guarantor's invitation to the closed group.
    /// </summary>
    public static FundMember Join(Guid fundId, Guid memberId)
    {
        if (fundId == Guid.Empty)
        {
            throw new InvalidMembershipException("Fund is required.");
        }

        if (memberId == Guid.Empty)
        {
            throw new InvalidMembershipException("Member is required.");
        }

        return new FundMember
        {
            Id = Guid.NewGuid(),
            FundId = fundId,
            MemberId = memberId,
            Status = MemberStatus.Active,
        };
    }

    public void Suspend()
    {
        if (Status == MemberStatus.Suspended)
        {
            throw new InvalidMembershipException("Member is already suspended.");
        }

        Status = MemberStatus.Suspended;
        Touch();
    }

    public void Reactivate()
    {
        if (Status == MemberStatus.Active)
        {
            throw new InvalidMembershipException("Member is already active.");
        }

        Status = MemberStatus.Active;
        Touch();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}