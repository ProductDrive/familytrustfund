namespace FamilyTrustFund.Domain.Payments;

/// <summary>
/// Validation exception for payment/recipient/disbursement operations.
/// </summary>
public class InvalidPaymentException : Exception
{
    public InvalidPaymentException(string message) : base(message)
    {
    }
}
