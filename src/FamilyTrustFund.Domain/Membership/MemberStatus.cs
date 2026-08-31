namespace FamilyTrustFund.Domain.Membership;

/// <summary>
/// Lifecycle state of a member within a specific fund.
/// </summary>
public enum MemberStatus
{
    /// <summary>An active member who can contribute, borrow and participate.</summary>
    Active = 1,

    /// <summary>Temporarily barred from participating by the Guarantor or an admin.</summary>
    Suspended = 2,
}