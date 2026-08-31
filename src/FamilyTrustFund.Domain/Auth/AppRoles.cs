namespace FamilyTrustFund.Domain.Auth;

/// <summary>
/// Well-known application roles.
/// Keep these server-authoritative; the frontend and APIs must never
/// derive authorization from client-supplied role claims.
/// </summary>
public static class AppRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Guarantor = "Guarantor";
    public const string Member = "Member";

    public static readonly string[] All = { SuperAdmin, Guarantor, Member };
}
