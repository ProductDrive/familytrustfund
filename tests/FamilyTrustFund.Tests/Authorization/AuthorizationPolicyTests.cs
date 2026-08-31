using System.Security.Claims;
using FamilyTrustFund.Api.Authorization;
using FamilyTrustFund.Domain.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTrustFund.Tests.Authorization;

public class AuthorizationPolicyTests
{
    private static IAuthorizationService CreateService() =>
        new ServiceCollection()
            .AddLogging()
            .AddAuthorizationBuilder()
            .AddApiPolicies()
            .Services
            .BuildServiceProvider()
            .GetRequiredService<IAuthorizationService>();

    private static ClaimsPrincipal Principal(params string[] roles)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }
        return new ClaimsPrincipal(identity);
    }

    [Theory]
    [InlineData(ApiPolicies.GuarantorOnly, AppRoles.Guarantor)]
    [InlineData(ApiPolicies.GuarantorOnly, AppRoles.SuperAdmin)]
    [InlineData(ApiPolicies.MemberOnly, AppRoles.Member)]
    [InlineData(ApiPolicies.MemberOnly, AppRoles.Guarantor)]
    [InlineData(ApiPolicies.MemberOnly, AppRoles.SuperAdmin)]
    [InlineData(ApiPolicies.AdminOnly, AppRoles.SuperAdmin)]
    public async Task Authorized_role_passes(string policy, string role)
    {
        var service = CreateService();
        var result = await service.AuthorizeAsync(Principal(role), resource: null, policy);
        result.Succeeded.Should().BeTrue($"role {role} should pass {policy}");
    }

    [Theory]
    [InlineData(ApiPolicies.GuarantorOnly, AppRoles.Member)]
    [InlineData(ApiPolicies.AdminOnly, AppRoles.Guarantor)]
    [InlineData(ApiPolicies.AdminOnly, AppRoles.Member)]
    public async Task Unauthorized_role_is_rejected(string policy, string role)
    {
        var service = CreateService();
        var result = await service.AuthorizeAsync(Principal(role), resource: null, policy);
        result.Succeeded.Should().BeFalse($"role {role} must not pass {policy}");
    }

    [Theory]
    [InlineData(ApiPolicies.GuarantorOnly)]
    [InlineData(ApiPolicies.AdminOnly)]
    [InlineData(ApiPolicies.MemberOnly)]
    public async Task Anonymous_user_is_rejected(string policy)
    {
        var service = CreateService();
        var result = await service.AuthorizeAsync(new ClaimsPrincipal(), resource: null, policy);
        result.Succeeded.Should().BeFalse($"{policy} must require an authenticated user");
    }
}
