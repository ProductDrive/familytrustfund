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

                var rolesAssigned = await AssignRolesAsync(
                    userManager,
                    user,
                    AppRoles.RolesForNewAccount(verification.RequestedRole),
                    logger);
                if (!rolesAssigned)
                {
                    // Roll the half-provisioned account back so the user can
                    // retry; signing in without a role would 403 everywhere.
                    await userManager.DeleteAsync(user);
                    return Results.BadRequest(new
                    {
                        code = "create_failed",
                        message = "We could not finish creating your account. Please try again.",
                    });
                }

                if (verification.RequestedRole == AppRoles.Guarantor)
                {
                    user.GuarantorApprovalStatus = GuarantorApprovalStatus.Pending;
                    var updateResult = await userManager.UpdateAsync(user);
                    if (!updateResult.Succeeded)
                    {
                        logger.LogError("Failed to mark new Guarantor {Email} as pending: {Errors}",
                            verification.Email,
                            string.Join("; ", updateResult.Errors.Select(e => e.Description)));
                        await userManager.DeleteAsync(user);
                        return Results.BadRequest(new
                        {
                            code = "create_failed",
                            message = "We could not finish creating your account. Please try again.",
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(verification.TermsVersion)
                        && !await termsRepository.ExistsAsync(user.Id, verification.TermsVersion, ct))
                    {
                        termsRepository.Add(TermsAcceptance.Create(user.Id, verification.TermsVersion));
                        await termsRepository.SaveChangesAsync(ct);
                    }

                    logger.LogInformation("Guarantor registration pending approval for {Email}.", verification.Email);
                }
                else
                {
                    logger.LogInformation("Member account created for {Email}.", verification.Email);
                }
            }
            else
            {
                // Earlier builds could create an account with no role at all,
                // which authenticates but then 403s on every endpoint. Heal
                // those accounts with the safe Member baseline; never assign a
                // privileged role based on the client's role picker.
                var existingRoles = await userManager.GetRolesAsync(user);
                if (existingRoles.Count == 0)
                {
                    logger.LogWarning(
                        "Existing account {Email} had no role; assigning the Member baseline.",
                        verification.Email);
                    if (!await AssignRolesAsync(userManager, user, new[] { AppRoles.Member }, logger))
                    {
                        return Results.BadRequest(new
                        {
                            code = "create_failed",
                            message = "We could not restore access to your account. Please contact support.",
                        });
                    }
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

    /// <summary>
    /// Assigns every role a brand-new account needs. Returns false (and logs)
    /// when Identity rejects an assignment so the caller can roll the account
    /// back instead of leaving a signed-in user with no permissions.
    /// </summary>
    private static async Task<bool> AssignRolesAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser user,
        IEnumerable<string> roles,
        ILogger logger)
    {
        foreach (var role in roles)
        {
            var result = await userManager.AddToRoleAsync(user, role);
            if (result.Succeeded)
            {
                continue;
            }

            logger.LogError("Failed to assign role {Role} to OTP user {Email}: {Errors}",
                role,
                user.Email,
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return false;
        }

        return true;
    }
}
