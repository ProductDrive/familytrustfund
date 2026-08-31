using FamilyTrustFund.Application.Membership;
using FamilyTrustFund.Domain.Membership;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FamilyTrustFund.Api.Membership;

public static class MembershipEndpoints
{
    public static IEndpointRouteBuilder MapMembershipEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/memberships")
            .RequireAuthorization(Policy);

        // Returns every fund the caller is a member of (member dashboard).
        group.MapGet("/mine", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            MembershipService membershipService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var memberships = await membershipService.GetMyMembershipsAsync(userId.Value, ct);
            return Results.Ok(memberships);
        });

        // Joins the caller to the fund identified by the Guarantor's join code.
        group.MapPost("/join", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            MembershipService membershipService,
            [FromBody] JoinFundRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var membership = await membershipService.JoinFundAsync(userId.Value, request, ct);
                return Results.Created($"/api/memberships/{membership.FundId}", membership);
            }
            catch (InvalidMembershipException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        return app;
    }

    private const string Policy = "MemberOnly";

    private static Guid? UserId(HttpContext http, UserManager<ApplicationUser> userManager)
    {
        var raw = userManager.GetUserId(http.User);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}