namespace FamilyTrustFund.Domain.Funds;

/// <summary>
/// Raised when a fund domain rule is violated. Messages are user-presentable.
/// </summary>
public class InvalidFundException : Exception
{
    public InvalidFundException(string message) : base(message) { }
}
