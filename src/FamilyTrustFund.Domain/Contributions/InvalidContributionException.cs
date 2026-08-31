namespace FamilyTrustFund.Domain.Contributions;

/// <summary>
/// Thrown when a contribution operation violates a domain invariant.
/// Messages are safe to display to end users.
/// </summary>
public sealed class InvalidContributionException : Exception
{
    public InvalidContributionException(string message)
        : base(message)
    {
    }
}
