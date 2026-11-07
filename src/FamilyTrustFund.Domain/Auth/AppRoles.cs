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

    /// <summary>
    /// Roles granted to a brand-new OTP account, based on the role selected at
    /// sign-up. The selection only influences a brand-new account; stored roles
    /// are always authoritative for an existing user (ADR-048). Every account
    /// must receive at least one role, otherwise it authenticates with no
    /// permissions and every role-protected endpoint returns 403.
    /// </summary>
    public static string[] RolesForNewAccount(string requestedRole) =>
        requestedRole == Guarantor
            ? new[] { Guarantor }
            : new[] { Member };
}
