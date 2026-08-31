using FamilyTrustFund.Domain.Auth;
using Microsoft.AspNetCore.Authorization;

namespace FamilyTrustFund.Api.Authorization;

public static class ApiPolicies
{
    public const string GuarantorOnly = "GuarantorOnly";
    public const string MemberOnly = "MemberOnly";
    public const string AdminOnly = "AdminOnly";

    public static AuthorizationBuilder AddApiPolicies(this AuthorizationBuilder builder) =>
        builder
            .AddPolicy(GuarantorOnly, p => p
                .RequireAuthenticatedUser()
                .RequireRole(AppRoles.Guarantor, AppRoles.SuperAdmin))
            .AddPolicy(MemberOnly, p => p
                .RequireAuthenticatedUser()
                .RequireRole(AppRoles.Member, AppRoles.Guarantor, AppRoles.SuperAdmin))
            .AddPolicy(AdminOnly, p => p
                .RequireAuthenticatedUser()
                .RequireRole(AppRoles.SuperAdmin));
}
