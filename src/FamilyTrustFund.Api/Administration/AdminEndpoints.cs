using FamilyTrustFund.Application.Membership;
using Microsoft.AspNetCore.Mvc;

namespace FamilyTrustFund.Api.Administration;

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

        return app;
    }

    private const string Policy = "AdminOnly";
}