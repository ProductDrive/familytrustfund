namespace FamilyTrustFund.Domain.Loans;

/// <summary>
/// Thrown when a loan operation violates a domain invariant.
/// Messages are safe to display to end users.
/// </summary>
public sealed class InvalidLoanException : Exception
{
    public InvalidLoanException(string message)
        : base(message)
    {
    }
}
