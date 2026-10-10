namespace FamilyTrustFund.Application.Notifications;

/// <summary>
/// Port for sending transactional email. Implemented by the infrastructure
/// layer (Afe.PRD.Email.Sender). Application code depends on this abstraction,
/// not on a concrete provider.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends the message. Throws <see cref="EmailDeliveryException"/> when the
    /// provider does not accept the message so callers can react (for example,
    /// discard an OTP that could never be delivered).
    /// </summary>
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}