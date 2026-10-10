namespace FamilyTrustFund.Application.Auth;

/// <summary>
/// Base for expected OTP sign-in rejections. <see cref="Code"/> is a stable,
/// non-sensitive machine identifier the API may return to the client; the
/// message is safe to display.
/// </summary>
public abstract class LoginOtpException : Exception
{
    protected LoginOtpException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>
/// The message is safe to surface to the end user. The API must never expose
/// provider diagnostics for this case.
/// </summary>
public sealed class OtpDeliveryException : LoginOtpException
{
    public OtpDeliveryException() : base("delivery_failed",
        "We could not send the code right now. Please try again shortly.")
    {
    }
}

/// <summary>Raised for validation failures when requesting or verifying a code.</summary>
public sealed class InvalidOtpRequestException : LoginOtpException
{
    public InvalidOtpRequestException(string code, string message) : base(code, message)
    {
    }

    public static InvalidOtpRequestException InvalidEmail() =>
        new("invalid_email", "Enter a valid email address.");

    public static InvalidOtpRequestException InvalidRole() =>
        new("invalid_role", "Choose whether you are joining as a member or a guarantor.");

    public static InvalidOtpRequestException TermsRequired() =>
        new("terms_required", "You must accept the guarantor terms to continue.");

    public static InvalidOtpRequestException TermsStale() =>
        new("terms_stale", "The terms have changed. Review and accept the latest version.");

    public static InvalidOtpRequestException Cooldown() =>
        new("cooldown", "A code was just sent. Please wait before requesting another.");

    public static InvalidOtpRequestException InvalidOrExpired() =>
        new("invalid_or_expired", "That code is not valid. Request a new one.");

    public static InvalidOtpRequestException Expired() =>
        new("expired", "That code has expired. Request a new one.");

    public static InvalidOtpRequestException TooManyAttempts() =>
        new("too_many_attempts", "Too many attempts. Request a new code.");

    public static InvalidOtpRequestException InvalidCode() =>
        new("invalid_code", "That code is incorrect. Please try again.");
}

/// <summary>
/// Server-authoritative outcome of a successful OTP verification: the identity
/// that proved ownership of the mailbox and the role/terms it requested.
/// </summary>
public sealed record LoginOtpVerification(string Email, string RequestedRole, string? TermsVersion);