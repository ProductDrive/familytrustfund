using System.Security.Claims;
using FamilyTrustFund.Application.Auth;
using FamilyTrustFund.Domain.Auth;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FamilyTrustFund.Api.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        // Sign-in capability discovery: the SPA decides whether to present the
        // email OTP flow and/or the Google button without hard-coding config.
        group.MapGet("/options", async (
            IOptions<LoginOtpOptions> otpOptions,
            IOptions<TermsOptions> termsOptions,
            IAuthenticationSchemeProvider schemes) =>
        {
            var googleConfigured = await schemes.GetSchemeAsync("Google") is not null;
            return Results.Ok(new AuthOptionsResponse
            {
                OtpEnabled = otpOptions.Value.Enabled,
                GoogleEnabled = googleConfigured,
                TermsVersion = termsOptions.Value.CurrentVersion,
            });
        });

        group.MapGet("/login", async (
            HttpContext ctx) =>
        {
            // In production the frontend always triggers Google sign-in
            // through the /challenge endpoint. This endpoint is kept for
            // discoverability/health of the auth flow.
            return Results.BadRequest(new { message = "Use /api/auth/google/challenge to sign in." });
        });

        group.MapGet("/google/challenge", async (
            HttpContext ctx,
            IAuthenticationSchemeProvider schemes) =>
        {
            if (await schemes.GetSchemeAsync("Google") is null)
            {
                return Results.BadRequest(new
                {
                    message = "Google sign-in is not configured. Use the development sign-in form instead.",
                });
            }

            var props = new AuthenticationProperties
            {
                RedirectUri = "/api/auth/google/callback",
            };
            return Results.Challenge(props, new[] { "Google" });
        });

        group.MapGet("/google/callback", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ILoggerFactory loggerFactory,
            IConfiguration configuration) =>
        {
            var logger = loggerFactory.CreateLogger("AuthEndpoints");
            var postLoginRedirect = configuration["Authentication:Google:PostLoginRedirect"] ?? "/";
            var info = await signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                logger.LogWarning("External login callback received but no external login info was found.");
                return Results.Unauthorized();
            }

            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
            {
                logger.LogWarning("External login did not provide an email claim.");
                return Results.BadRequest(new { message = "Google did not return an email address." });
            }

            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    DisplayName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? email,
                    EmailConfirmed = true,
                };
                var createResult = await userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    logger.LogWarning("Failed to provision Google user {Email}: {Errors}",
                        email, string.Join("; ", createResult.Errors.Select(e => e.Description)));
                    return Results.BadRequest(new { message = "Could not create account." });
                }
                // Default role for a newly provisioned user. Role changes are
                // managed by Guarantors/SuperAdmins through membership flows.
                await userManager.AddToRoleAsync(user, AppRoles.Member);
            }

            var login = new UserLoginInfo(info.LoginProvider, info.ProviderKey, info.LoginProvider);
            var externalResult = await signInManager.ExternalLoginSignInAsync(
                info.LoginProvider, info.ProviderKey, isPersistent: true, bypassTwoFactor: true);
            if (externalResult.Succeeded)
            {
                await signInManager.UpdateExternalAuthenticationTokensAsync(info);
                return Results.Redirect(postLoginRedirect);
            }

            if (externalResult.IsLockedOut)
            {
                return Results.Redirect(postLoginRedirect + "?error=lockedout");
            }

            var addResult = await userManager.AddLoginAsync(user, login);
            if (!addResult.Succeeded)
            {
                return Results.BadRequest(new { message = "Could not link external login." });
            }
            await signInManager.SignInAsync(user, isPersistent: true);
            return Results.Redirect(postLoginRedirect);
        });

        group.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.NoContent();
        });

        group.MapGet("/me", async (HttpContext http, UserManager<ApplicationUser> userManager) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            var roles = await userManager.GetRolesAsync(user);
            return Results.Ok(new CurrentUserResponse
            {
                Id = user.Id,
                Email = user.Email!,
                DisplayName = user.DisplayName,
                Roles = roles.ToArray(),
                GuarantorApprovalStatus = user.GuarantorApprovalStatus.ToString(),
            });
        });

        return app;
    }

    public sealed record AuthOptionsResponse
    {
        public bool OtpEnabled { get; init; }
        public bool GoogleEnabled { get; init; }
        public string TermsVersion { get; init; } = string.Empty;
    }

    public sealed record CurrentUserResponse
    {
        public Guid Id { get; init; }
        public string Email { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string[] Roles { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Guarantor approval state (e.g. "Pending") so the UI can explain that
        /// a guarantor account is awaiting a Super Admin decision.
        /// </summary>
        public string GuarantorApprovalStatus { get; init; } = string.Empty;
    }
}
