namespace FamilyTrustFund.Domain.Membership;

/// <summary>
/// Thrown when a membership operation violates a domain rule.
/// </summary>
public sealed class InvalidMembershipException : Exception
{
    public InvalidMembershipException(string message) : base(message)
    {
    }
}