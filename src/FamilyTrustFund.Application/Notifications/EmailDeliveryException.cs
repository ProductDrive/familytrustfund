namespace FamilyTrustFund.Application.Notifications;

/// <summary>
/// Raised when the email provider does not accept a message. Callers must not
/// expose provider diagnostics to end users.
/// </summary>
public sealed class EmailDeliveryException : Exception
{
    public EmailDeliveryException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}