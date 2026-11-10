namespace FamilyTrustFund.Domain.Funds;

/// <summary>
/// Raised when a Group Guarantor invitation or membership operation violates a
/// business rule. Messages are safe to show to the user.
/// </summary>
public class InvalidFundGuarantorException : Exception
{
    public InvalidFundGuarantorException(string message) : base(message)
    {
    }
}
