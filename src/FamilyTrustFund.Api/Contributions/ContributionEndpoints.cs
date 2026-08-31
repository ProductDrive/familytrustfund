using FamilyTrustFund.Application.Contributions;
using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Domain.Contributions;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FamilyTrustFund.Api.Contributions;

public static class ContributionEndpoints
{
    public static IEndpointRouteBuilder MapContributionEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Member endpoints ──────────────────────────────────────────────

        var memberGroup = app.MapGroup("/api/contributions")
            .RequireAuthorization("MemberOnly");

        // Report a contribution (Member).
        memberGroup.MapPost("/report", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ContributionService contributionService,
            [FromBody] ReportContributionRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var contribution = await contributionService.ReportContributionAsync(userId.Value, request, ct);
                return Results.Created($"/api/contributions/{contribution.Id}", contribution);
            }
            catch (InvalidContributionException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // My contributions across all funds (Member).
        memberGroup.MapGet("/mine", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ContributionService contributionService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var contributions = await contributionService.GetMyContributionsAsync(userId.Value, ct);
            return Results.Ok(contributions);
        });

        // My Fund Credit summary across funds (Member).
        memberGroup.MapGet("/fund-credit", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ContributionService contributionService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var summaries = await contributionService.GetMyFundCreditAsync(userId.Value, ct);
            return Results.Ok(summaries);
        });

        // ── Guarantor endpoints ───────────────────────────────────────────

        var guarantorGroup = app.MapGroup("/api/guarantor/contributions")
            .RequireAuthorization("GuarantorOnly");

        // Pending contribution confirmation queue (Guarantor).
        guarantorGroup.MapGet("/pending", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ContributionService contributionService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var contributions = await contributionService.GetPendingForGuarantorAsync(userId.Value, ct);
            return Results.Ok(contributions);
        });

        // All contributions for a fund (Guarantor — ownership-scoped).
        guarantorGroup.MapGet("/fund/{fundId:guid}", async (
            Guid fundId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundService fundService,
            ContributionService contributionService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var fund = await fundService.GetFundForGuarantorAsync(userId.Value, fundId, ct);
            if (fund is null)
            {
                return Results.NotFound();
            }

            var contributions = await contributionService.GetByFundAsync(fundId, ct);
            return Results.Ok(contributions);
        });

        // Confirm a contribution (Guarantor).
        guarantorGroup.MapPost("/confirm", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ContributionService contributionService,
            [FromBody] ConfirmContributionRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var contribution = await contributionService.ConfirmContributionAsync(userId.Value, request, ct);
                return Results.Ok(contribution);
            }
            catch (InvalidContributionException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // Reject a contribution (Guarantor).
        guarantorGroup.MapPost("/reject", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ContributionService contributionService,
            [FromBody] RejectContributionRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var contribution = await contributionService.RejectContributionAsync(userId.Value, request, ct);
                return Results.Ok(contribution);
            }
            catch (InvalidContributionException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        return app;
    }

    private static Guid? UserId(HttpContext http, UserManager<ApplicationUser> userManager)
    {
        var raw = userManager.GetUserId(http.User);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
