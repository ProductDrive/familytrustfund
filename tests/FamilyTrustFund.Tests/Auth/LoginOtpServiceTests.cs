using System.Text.RegularExpressions;
using FamilyTrustFund.Application.Auth;
using FamilyTrustFund.Application.Notifications;
using FamilyTrustFund.Domain.Auth;
using FamilyTrustFund.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace FamilyTrustFund.Tests.Auth;

public class LoginOtpServiceTests
{
    private const string Email = "user@example.com";

    private static (LoginOtpService Service, FakeLoginOtpRepository Repo, FakeEmailSender Email) Build(
        LoginOtpOptions? options = null,
        TermsOptions? terms = null)
    {
        var repo = new FakeLoginOtpRepository();
        var email = new FakeEmailSender();
        var service = new LoginOtpService(
            repo,
            email,
            Options.Create(options ?? new LoginOtpOptions()),
            Options.Create(terms ?? new TermsOptions { CurrentVersion = "1.0" }));
        return (service, repo, email);
    }

    private static string ExtractCode(EmailMessage message) =>
        Regex.Match(message.Body, @"\b\d{6}\b").Value;

    private static string WrongCode(string real) => real == "000000" ? "111111" : "000000";

    [Fact]
    public async Task Request_for_member_sends_code_and_stores_only_a_hash()
    {
        var (service, repo, email) = Build();

        await service.RequestAsync(Email, AppRoles.Member, termsAccepted: false, termsVersion: null);

        var otp = repo.Items.Should().ContainSingle().Subject;
        otp.Email.Should().Be(Email);
        otp.RequestedRole.Should().Be(AppRoles.Member);
        otp.TermsVersion.Should().BeNull();
        otp.CodeHash.Should().NotBeNullOrEmpty();
        otp.CodeSalt.Should().NotBeNullOrEmpty();

        var sent = email.Sent.Should().ContainSingle().Subject;
        sent.To.Should().Be(Email);
        sent.Subject.Should().Be("FamilyTrustFund OTP");

        var code = ExtractCode(sent);
        code.Should().MatchRegex("^\\d{6}$");
        otp.CodeHash.Should().NotBe(code);
    }

    [Fact]
    public async Task Request_normalizes_the_email()
    {
        var (service, repo, _) = Build();

        await service.RequestAsync("  User@Example.COM ", AppRoles.Member, false, null);

        repo.Items.Single().Email.Should().Be(Email);
    }

    [Fact]
    public async Task Guarantor_without_accepting_terms_is_rejected()
    {
        var (service, _, _) = Build();

        var act = () => service.RequestAsync(Email, AppRoles.Guarantor, termsAccepted: false, termsVersion: "1.0");

        await act.Should().ThrowAsync<InvalidOtpRequestException>()
            .Where(e => e.Code == "terms_required");
    }

    [Fact]
    public async Task Guarantor_with_a_stale_terms_version_is_rejected()
    {
        var (service, _, _) = Build();

        var act = () => service.RequestAsync(Email, AppRoles.Guarantor, termsAccepted: true, termsVersion: "0.9");

        await act.Should().ThrowAsync<InvalidOtpRequestException>()
            .Where(e => e.Code == "terms_stale");
    }

    [Fact]
    public async Task Guarantor_with_the_current_terms_records_role_and_version()
    {
        var (service, repo, _) = Build();

        await service.RequestAsync(Email, AppRoles.Guarantor, termsAccepted: true, termsVersion: "1.0");

        var otp = repo.Items.Single();
        otp.RequestedRole.Should().Be(AppRoles.Guarantor);
        otp.TermsVersion.Should().Be("1.0");
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    [InlineData("user@")]
    [InlineData("@nodomain")]
    public async Task Request_with_an_invalid_email_is_rejected(string bad)
    {
        var (service, _, _) = Build();

        var act = () => service.RequestAsync(bad, AppRoles.Member, false, null);

        await act.Should().ThrowAsync<InvalidOtpRequestException>()
            .Where(e => e.Code == "invalid_email");
    }

    [Fact]
    public async Task Request_with_an_invalid_role_is_rejected()
    {
        var (service, _, _) = Build();

        var act = () => service.RequestAsync(Email, AppRoles.SuperAdmin, false, null);

        await act.Should().ThrowAsync<InvalidOtpRequestException>()
            .Where(e => e.Code == "invalid_role");
    }

    [Fact]
    public async Task Request_within_the_resend_cooldown_is_rejected()
    {
        var (service, repo, _) = Build(new LoginOtpOptions { ResendCooldownSeconds = 60 });

        await service.RequestAsync(Email, AppRoles.Member, false, null);
        var act = () => service.RequestAsync(Email, AppRoles.Member, false, null);

        await act.Should().ThrowAsync<InvalidOtpRequestException>()
            .Where(e => e.Code == "cooldown");
        repo.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Request_discards_the_code_when_the_email_cannot_be_sent()
    {
        var (service, repo, email) = Build();
        email.FailWith = new InvalidOperationException("smtp unavailable");

        var act = () => service.RequestAsync(Email, AppRoles.Member, false, null);

        await act.Should().ThrowAsync<OtpDeliveryException>();
        repo.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Verify_with_the_correct_code_consumes_it_and_returns_the_verification()
    {
        var (service, repo, email) = Build();
        await service.RequestAsync(Email, AppRoles.Guarantor, termsAccepted: true, termsVersion: "1.0");
        var code = ExtractCode(email.Sent[0]);

        var result = await service.VerifyAsync(Email, code);

        result.Email.Should().Be(Email);
        result.RequestedRole.Should().Be(AppRoles.Guarantor);
        result.TermsVersion.Should().Be("1.0");
        repo.Items.Single().IsConsumed.Should().BeTrue();

        var reuse = () => service.VerifyAsync(Email, code);
        await reuse.Should().ThrowAsync<InvalidOtpRequestException>()
            .Where(e => e.Code == "invalid_or_expired");
    }

    [Fact]
    public async Task Verify_with_a_wrong_code_records_a_failed_attempt()
    {
        var (service, repo, email) = Build();
        await service.RequestAsync(Email, AppRoles.Member, false, null);
        var real = ExtractCode(email.Sent[0]);

        var act = () => service.VerifyAsync(Email, WrongCode(real));

        await act.Should().ThrowAsync<InvalidOtpRequestException>()
            .Where(e => e.Code == "invalid_code");
        repo.Items.Single().AttemptCount.Should().Be(1);
        repo.Items.Single().IsConsumed.Should().BeFalse();
    }

    [Fact]
    public async Task Verify_beyond_the_attempt_limit_is_rejected()
    {
        var (service, _, email) = Build(new LoginOtpOptions { MaxAttempts = 1 });
        await service.RequestAsync(Email, AppRoles.Member, false, null);
        var real = ExtractCode(email.Sent[0]);

        var first = () => service.VerifyAsync(Email, WrongCode(real));
        await first.Should().ThrowAsync<InvalidOtpRequestException>()
            .Where(e => e.Code == "invalid_code");

        var second = () => service.VerifyAsync(Email, WrongCode(real));
        await second.Should().ThrowAsync<InvalidOtpRequestException>()
            .Where(e => e.Code == "too_many_attempts");
    }

    [Fact]
    public async Task Verify_an_expired_code_is_rejected()
    {
        var (service, _, email) = Build(new LoginOtpOptions { LifetimeMinutes = -1 });
        await service.RequestAsync(Email, AppRoles.Member, false, null);
        var code = ExtractCode(email.Sent[0]);

        var act = () => service.VerifyAsync(Email, code);

        await act.Should().ThrowAsync<InvalidOtpRequestException>()
            .Where(e => e.Code == "expired");
    }

    [Fact]
    public async Task Verify_an_unknown_email_is_rejected()
    {
        var (service, _, _) = Build();

        var act = () => service.VerifyAsync(Email, "123456");

        await act.Should().ThrowAsync<InvalidOtpRequestException>()
            .Where(e => e.Code == "invalid_or_expired");
    }

    [Fact]
    public async Task Only_the_most_recent_code_is_valid()
    {
        var (service, _, email) = Build(new LoginOtpOptions { ResendCooldownSeconds = 0 });
        await service.RequestAsync(Email, AppRoles.Member, false, null);
        var firstCode = ExtractCode(email.Sent[0]);
        await service.RequestAsync(Email, AppRoles.Member, false, null);
        var secondCode = ExtractCode(email.Sent[1]);

        var stale = () => service.VerifyAsync(Email, firstCode);
        await stale.Should().ThrowAsync<InvalidOtpRequestException>()
            .Where(e => e.Code == "invalid_code");

        var result = await service.VerifyAsync(Email, secondCode);
        result.Email.Should().Be(Email);
    }
}