using FamilyTrustFund.Application.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PD.EmailSender.Helpers;
using PD.EmailSender.Helpers.Model;

namespace FamilyTrustFund.Infrastructure.Notifications;

/// <summary>
/// <see cref="IEmailSender"/> backed by the Afe.PRD.Email.Sender package,
/// using its "on behalf" sender. Isolates the rest of the application from the
/// vendor API (AGENTS §3).
/// </summary>
public sealed class AfeEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<AfeEmailSender> _logger;

    public AfeEmailSender(IOptions<EmailOptions> options, ILogger<AfeEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            // Deliberately does not log the recipient or body (PII / codes).
            _logger.LogWarning("Email sending is disabled; message not delivered.");
            return;
        }

        var model = new MessageModel
        {
            Subject = message.Subject,
            Message = message.Body,
            EmailDisplayName = message.DisplayName ?? _options.FromDisplayName,
            MessageType = PDMessageType.Email,
            Contacts = new List<ContactsModel>
            {
                new() { Email = message.To },
            },
            SenderSettings = new SenderSettingsDTO { OnBehalf = true },
            FallBackSenderSettings = new SenderSettingsDTO { OnBehalf = true },
        };

        bool sent;
        try
        {
            sent = await SendMailVTwo.SendSingleEmailOnBehalf(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email provider call failed.");
            throw new EmailDeliveryException("The email provider could not be reached.", ex);
        }

        if (!sent)
        {
            throw new EmailDeliveryException("The email provider did not accept the message.");
        }
    }
}