using FamilyTrustFund.Application.Notifications;
using FamilyTrustFund.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FamilyTrustFund.Tests.Notifications;

public class AfeEmailSenderTests
{
    // Constructing the sender and entering SendAsync loads the vendor
    // Afe.PRD.Email.Sender assembly under the application's .NET version. The
    // disabled path returns before any network call, so this also proves the
    // package's type references resolve on the host runtime.
    [Fact]
    public async Task Disabled_sender_skips_without_contacting_the_provider()
    {
        var sender = new AfeEmailSender(
            Options.Create(new EmailOptions { Enabled = false }),
            NullLogger<AfeEmailSender>.Instance);

        var act = () => sender.SendAsync(new EmailMessage
        {
            To = "user@example.com",
            Subject = "Test",
            Body = "Body",
        });

        await act.Should().NotThrowAsync();
    }
}