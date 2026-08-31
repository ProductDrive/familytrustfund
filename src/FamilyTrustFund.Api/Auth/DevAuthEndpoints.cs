using System.Security.Claims;
using FamilyTrustFund.Domain.Auth;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace FamilyTrustFund.Api.Auth;

public sealed class DevAuthOptions
{
    /// <summary>When true, the development-only sign-in endpoint is enabled.</summary>
    public bool UseDevAuth { get; set; }

    /// <summary>True when a Google OIDC client is configured.</summary>
    public bool GoogleConfigured { get; set; }
}

public sealed record DevLoginRequest
{
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = AppRoles.Member;
}

/// <summary>
/// Development-only authentication used before a Google OIDC client is
/// provisioned. Exercises the real Identity + cookie + role pipeline.
/// Never enabled outside development.
/// </summary>
public static class DevAuthEndpoints
{
    public static IEndpointRouteBuilder MapDevAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/dev");

        // Lets the frontend decide which sign-in options to show.
        group.MapGet("/status", (IOptions<DevAuthOptions> options) =>
            Results.Ok(new
            {
                enabled = options.Value.UseDevAuth,
                googleEnabled = options.Value.GoogleConfigured,
            }));

        group.MapPost("/login", async (
            DevLoginRequest request,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IOptions<DevAuthOptions> options,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("DevAuthEndpoints");
            if (!options.Value.UseDevAuth)
            {
                return Results.NotFound();
            }

            var email = request.Email.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email))
            {
                return Results.BadRequest(new { message = "Email is required." });
            }

            if (!AppRoles.All.Contains(request.Role))
            {
                return Results.BadRequest(new { message = "Invalid role." });
            }

            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? email : request.DisplayName.Trim(),
                    EmailConfirmed = true,
                };
                var createResult = await userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    return Results.BadRequest(new
                    {
                        message = string.Join("; ", createResult.Errors.Select(e => e.Description)),
                    });
                }
            }

            // Deterministic dev role selection: replace all roles with the
            // single requested role so dev sign-ins do not accumulate roles
            // (e.g. a Member who also logged in as Guarantor stays Member).
            var currentRoles = await userManager.GetRolesAsync(user);
            foreach (var role in currentRoles)
            {
                await userManager.RemoveFromRoleAsync(user, role);
            }
            await userManager.AddToRoleAsync(user, request.Role);

            var userPrincipal = await signInManager.CreateUserPrincipalAsync(user);
            var roles = await userManager.GetRolesAsync(user);
            await signInManager.SignInAsync(user, isPersistent: true);

            logger.LogInformation("Dev auth sign-in for {Email} with roles [{Roles}]", email, string.Join(",", roles));

            return Results.Ok(new { id = user.Id, email = user.Email, displayName = user.DisplayName, roles });
        });

        return app;
    }
}
