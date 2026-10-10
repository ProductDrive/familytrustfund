namespace FamilyTrustFund.Infrastructure.Notifications;

/// <summary>
/// Configuration for transactional email. Read from "Email". The underlying
/// Afe.PRD.Email.Sender uses an "on behalf" sender when
/// <see cref="Enabled"/> is true, so no per-environment SMTP credentials are
/// required here.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// When false, sends are skipped and logged instead of failing. Defaults to
    /// true so a misconfiguration is visible rather than silently swallowing
    /// sign-in codes.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Display name shown to recipients when the message omits one.</summary>
    public string FromDisplayName { get; set; } = "Family Trust Fund";
}