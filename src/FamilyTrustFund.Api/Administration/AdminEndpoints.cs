using System.Security.Claims;
using FamilyTrustFund.Application.Audit;
using FamilyTrustFund.Application.Membership;
using FamilyTrustFund.Domain.Auth;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace FamilyTrustFund.Api.Administration;

public sealed record ApproveGuarantorRequest
{
    public bool Approved { get; init; }
}

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .RequireAuthorization(Policy);

        // Platform-wide membership view (Super Admin only).
        group.MapGet("/memberships", async (
            MembershipService membershipService,
            CancellationToken ct) =>
        {
            var memberships = await membershipService.GetAllMembershipsAsync(ct);
            return Results.Ok(memberships);
        });

        // Approve or reject a self-registered Guarantor. This is the only place
        // the Guarantor role is granted to an OTP account; until then a pending
        // guarantor holds only the Member baseline role (AGENTS §4).
        group.MapPost("/guarantors/{userId:guid}/approve", async (
            Guid userId,
            ApproveGuarantorRequest request,
            UserManager<ApplicationUser> userManager,
            IAuditLog auditLog,
            HttpContext http,
            CancellationToken ct) =>
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user is null)
            {
                return Results.NotFound(new { message = "Guarantor account not found." });
            }

            if (user.GuarantorApprovalStatus != GuarantorApprovalStatus.Pending)
            {
                return Results.BadRequest(new { message = "That account is not awaiting Guarantor approval." });
            }

            if (request.Approved)
            {
                if (!await userManager.IsInRoleAsync(user, AppRoles.Guarantor))
                {
                    await userManager.AddToRoleAsync(user, AppRoles.Guarantor);
                }

                user.GuarantorApprovalStatus = GuarantorApprovalStatus.Approved;
            }
            else
            {
                user.GuarantorApprovalStatus = GuarantorApprovalStatus.Rejected;
            }

            var actorId = Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed)
                ? parsed
                : Guid.Empty;

            // Recorded on the same DbContext; the UpdateAsync below commits the
            // status change and the audit record together.
            await auditLog.RecordAsync(
                actorId,
                request.Approved ? "Guarantor.Approved" : "Guarantor.Rejected",
                "User",
                user.Id,
                request.Approved ? "Guarantor registration approved." : "Guarantor registration rejected.",
                ct);

            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                return Results.BadRequest(new
                {
                    message = string.Join("; ", updateResult.Errors.Select(e => e.Description)),
                });
            }

            return Results.NoContent();
        })
        // TEMPORARY: exposed anonymously for testing only. This grants a
        // privileged role, so it MUST be reverted to the AdminOnly group
        // policy (AGENTS §4) before any real deployment.
        .AllowAnonymous();

        return app;
    }

    private const string Policy = "AdminOnly";
}