namespace FamilyTrustFund.Application.Auth;

/// <summary>
/// Configuration for passwordless email sign-in. Read from
/// "Authentication:Otp". Disabled by default; enabled per environment.
/// </summary>
public sealed class LoginOtpOptions
{
    public const string SectionName = "Authentication:Otp";

    /// <summary>When false the OTP endpoints are not mapped.</summary>
    public bool Enabled { get; set; }

    /// <summary>Number of decimal digits in the code.</summary>
    public int CodeLength { get; set; } = 6;

    /// <summary>Minutes a code remains valid.</summary>
    public int LifetimeMinutes { get; set; } = 5;

    /// <summary>Maximum failed verification attempts before a code is void.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Minimum seconds between issuing codes to the same email.</summary>
    public int ResendCooldownSeconds { get; set; } = 60;
}