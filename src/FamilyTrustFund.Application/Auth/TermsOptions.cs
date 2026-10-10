namespace FamilyTrustFund.Application.Auth;

/// <summary>
/// Configuration for the Guarantor terms of service. Read from
/// "Authentication:Terms".
/// </summary>
public sealed class TermsOptions
{
    public const string SectionName = "Authentication:Terms";

    /// <summary>The terms version a new Guarantor must accept.</summary>
    public string CurrentVersion { get; set; } = "1.0";
}