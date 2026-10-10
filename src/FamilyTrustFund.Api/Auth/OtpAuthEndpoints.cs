using System.Security.Claims;
using FamilyTrustFund.Application.Auth;
using FamilyTrustFund.Application.Notifications;
using FamilyTrustFund.Domain.Auth;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace FamilyTrustFund.Api.Auth;

public sealed record OtpRequestRequest
{
    public string Email { get; init; } = string.Empty;
    public string Role { get; init; } = AppRoles.Member;
    public bool TermsAccepted { get; init; }
    public string? TermsVersion { get; init; }
}

public sealed record OtpVerifyRequest
{
    public string Email { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
}

/// <summary>
/// Passwordless email sign-in. Codes are issued and verified by
/// <see cref="LoginOtpService"/>; this layer only translates HTTP and performs
/// the Identity provisioning/sign-in. Client-supplied roles are never trusted:
/// for an existing account the stored roles always win.
/// </summary>
public static class OtpAuthEndpoints
{
    public const string RequestPolicy = "otp-request";
    public const string VerifyPolicy = "otp-verify";

    public static IEndpointRouteBuilder MapOtpAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/otp");

        group.MapPost("/request", async (
            OtpRequestRequest request,
            LoginOtpService service,
            CancellationToken ct) =>
        {
            try
            {
                await service.RequestAsync(
                    request.Email,
                    request.Role,
                    request.TermsAccepted,
                    request.TermsVersion,
                    ct);
                return Results.NoContent();
            }
            catch (LoginOtpException ex)
            {
                return Results.BadRequest(new { code = ex.Code, message = ex.Message });
            }
        })
        .RequireRateLimiting(RequestPolicy);

        group.MapPost("/verify", async (
            OtpVerifyRequest request,
            LoginOtpService service,
            UserManager<ApplicationUser> userManager,
            ITermsAcceptanceRepository termsRepository,
            SignInManager<ApplicationUser> signInManager,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("OtpAuthEndpoints");

            LoginOtpVerification verification;
            try
            {
                verification = await service.VerifyAsync(request.Email, request.Code, ct);
            }
            catch (LoginOtpException ex)
            {
                return Results.BadRequest(new { code = ex.Code, message = ex.Message });
            }

            var user = await userManager.FindByEmailAsync(verification.Email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = verification.Email,
                    Email = verification.Email,
                    DisplayName = verification.Email,
                    EmailConfirmed = true,
                };

                var createResult = await userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    logger.LogWarning("Failed to provision OTP user {Email}: {Errors}",
                        verification.Email,
                        string.Join("; ", createResult.Errors.Select(e => e.Description)));
                    return Results.BadRequest(new
                    {
                        code = "create_failed",
                        message = "We could not create your account. Please try again.",
                    });
                }

                // Every new account starts as a Member. A Guarantor self-signup
                // additionally becomes a pending approval (AGENTS §4): the
                // Guarantor role is granted later by a Super Admin, never here.
                await userManager.AddToRoleAsync(user, AppRoles.Member);

                if (verification.RequestedRole == AppRoles.Guarantor)
                {
                    user.GuarantorApprovalStatus = GuarantorApprovalStatus.Pending;
                    await userManager.UpdateAsync(user);

                    if (!string.IsNullOrWhiteSpace(verification.TermsVersion)
                        && !await termsRepository.ExistsAsync(user.Id, verification.TermsVersion, ct))
                    {
                        termsRepository.Add(TermsAcceptance.Create(user.Id, verification.TermsVersion));
                        await termsRepository.SaveChangesAsync(ct);
                    }

                    logger.LogInformation("Guarantor registration pending approval for {Email}.", verification.Email);
                }
            }

            await signInManager.SignInAsync(user, isPersistent: true);

            var roles = await userManager.GetRolesAsync(user);
            return Results.Ok(new AuthEndpoints.CurrentUserResponse
            {
                Id = user.Id,
                Email = user.Email!,
                DisplayName = user.DisplayName,
                Roles = roles.ToArray(),
                GuarantorApprovalStatus = user.GuarantorApprovalStatus.ToString(),
            });
        })
        .RequireRateLimiting(VerifyPolicy);

        return app;
    }
}