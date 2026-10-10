using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using FamilyTrustFund.Application.Notifications;
using FamilyTrustFund.Domain.Auth;
using Microsoft.Extensions.Options;

namespace FamilyTrustFund.Application.Auth;

/// <summary>
/// Issues and verifies passwordless email sign-in codes. All validation,
/// cooldowns, attempt limits and hashing are enforced here so endpoints stay
/// thin and the rules are unit-testable.
/// </summary>
public class LoginOtpService
{
    private readonly ILoginOtpRepository _repository;
    private readonly IEmailSender _emailSender;
    private readonly LoginOtpOptions _options;
    private readonly TermsOptions _termsOptions;

    public LoginOtpService(
        ILoginOtpRepository repository,
        IEmailSender emailSender,
        IOptions<LoginOtpOptions> options,
        IOptions<TermsOptions> termsOptions)
    {
        _repository = repository;
        _emailSender = emailSender;
        _options = options.Value;
        _termsOptions = termsOptions.Value;
    }

    /// <summary>
    /// Validates the request, stores a fresh hashed code and emails it to the
    /// requester. Only the most recent unconsumed code per email is valid.
    /// </summary>
    public async Task RequestAsync(
        string email,
        string role,
        bool termsAccepted,
        string? termsVersion,
        CancellationToken ct = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (!IsValidEmail(normalizedEmail))
        {
            throw InvalidOtpRequestException.InvalidEmail();
        }

        if (role != AppRoles.Member && role != AppRoles.Guarantor)
        {
            throw InvalidOtpRequestException.InvalidRole();
        }

        string? acceptedVersion = null;
        if (role == AppRoles.Guarantor)
        {
            if (!termsAccepted)
            {
                throw InvalidOtpRequestException.TermsRequired();
            }

            if (!string.Equals(termsVersion, _termsOptions.CurrentVersion, StringComparison.Ordinal))
            {
                throw InvalidOtpRequestException.TermsStale();
            }

            acceptedVersion = _termsOptions.CurrentVersion;
        }

        var now = DateTime.UtcNow;
        var latest = await _repository.GetLatestByEmailAsync(normalizedEmail, ct);
        if (latest is not null
            && !latest.IsExpiredAt(now)
            && latest.CreatedAtUtc.AddSeconds(_options.ResendCooldownSeconds) > now)
        {
            throw InvalidOtpRequestException.Cooldown();
        }

        var code = GenerateCode(_options.CodeLength);
        var salt = GenerateSalt();
        var otp = LoginOtp.Issue(
            normalizedEmail,
            role,
            acceptedVersion,
            Hash(code, salt),
            salt,
            now.AddMinutes(_options.LifetimeMinutes),
            _options.MaxAttempts);

        // Retire any outstanding codes so only the newest works, then persist
        // before sending: a failed send below deletes exactly this row.
        var outstanding = await _repository.GetUnconsumedByEmailAsync(normalizedEmail, ct);
        _repository.RemoveRange(outstanding);
        _repository.Add(otp);
        await _repository.SaveChangesAsync(ct);

        try
        {
            await _emailSender.SendAsync(
                new EmailMessage
                {
                    To = normalizedEmail,
                    Subject = "FamilyTrustFund OTP",
                    Body = BuildBody(code, _options.LifetimeMinutes),
                    DisplayName = "Family Trust Fund",
                },
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The code could never reach the user; do not leave it valid.
            _repository.RemoveRange(new[] { otp });
            await _repository.SaveChangesAsync(ct);
            throw new OtpDeliveryException();
        }
    }

    /// <summary>
    /// Verifies a code. Consumes it on success; records failed attempts and
    /// voids a code once the attempt limit is reached. Throws
    /// <see cref="InvalidOtpRequestException"/> on any failure.
    /// </summary>
    public async Task<LoginOtpVerification> VerifyAsync(
        string email,
        string code,
        CancellationToken ct = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (!IsValidEmail(normalizedEmail) || string.IsNullOrWhiteSpace(code))
        {
            throw InvalidOtpRequestException.InvalidOrExpired();
        }

        var otp = await _repository.GetLatestUnconsumedAsync(normalizedEmail, ct);
        if (otp is null)
        {
            throw InvalidOtpRequestException.InvalidOrExpired();
        }

        var now = DateTime.UtcNow;
        if (otp.IsExpiredAt(now))
        {
            throw InvalidOtpRequestException.Expired();
        }

        if (otp.HasExceededAttempts)
        {
            throw InvalidOtpRequestException.TooManyAttempts();
        }

        if (!otp.MatchesCode(Hash(code.Trim(), otp.CodeSalt)))
        {
            otp.RecordFailedAttempt();
            await _repository.SaveChangesAsync(ct);
            throw InvalidOtpRequestException.InvalidCode();
        }

        otp.Consume(now);
        await _repository.SaveChangesAsync(ct);

        return new LoginOtpVerification(otp.Email, otp.RequestedRole, otp.TermsVersion);
    }

    private static string NormalizeEmail(string email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();

    private static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email)
        && email.Length <= 256
        && MailAddress.TryCreate(email, out var parsed)
        && string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase);

    private static string GenerateCode(int length)
    {
        length = Math.Clamp(length, 4, 10);
        var maxExclusive = (int)Math.Pow(10, length);
        var value = RandomNumberGenerator.GetInt32(maxExclusive);
        return value.ToString(CultureInfo.InvariantCulture).PadLeft(length, '0');
    }

    private static string GenerateSalt()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string Hash(string code, string salt)
    {
        var material = Encoding.UTF8.GetBytes(salt + "|" + code);
        var hash = SHA256.HashData(material);
        return Convert.ToBase64String(hash);
    }

    private static string BuildBody(string code, int lifetimeMinutes) =>
        $"""
        <p>Your Family Trust Fund sign-in code is:</p>
        <p style="font-size:24px;font-weight:700;letter-spacing:4px;">{code}</p>
        <p>This code expires in {lifetimeMinutes} minutes. If you did not request it, you can ignore this email.</p>
        """;
}