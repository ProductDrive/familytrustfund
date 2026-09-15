using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Application.Membership;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FamilyTrustFund.Api.Funds;

public static class FundEndpoints
{
    public static IEndpointRouteBuilder MapFundEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/funds")
            .RequireAuthorization(Policy);

        group.MapGet("/", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundService fundService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var funds = await fundService.GetFundsForGuarantorAsync(userId.Value, ct);
            return Results.Ok(funds.Select(FundDtoMapper.Map));
        });

        group.MapPost("/", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundService fundService,
            [FromBody] CreateFundRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { message = "Fund name is required." });
            }

            try
            {
                var fund = await fundService.CreateFundAsync(userId.Value, request, ct);
                return Results.Created($"/api/funds/{fund.Id}", FundDtoMapper.Map(fund));
            }
            catch (Domain.Funds.InvalidFundException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundService fundService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var fund = await fundService.GetFundForGuarantorAsync(userId.Value, id, ct);
            return fund is null ? Results.NotFound() : Results.Ok(FundDtoMapper.Map(fund));
        });

        // Members of a specific fund (Guarantor, ownership-scoped).
        group.MapGet("/{id:guid}/members", async (
            Guid id,
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

            var members = await membershipService.GetFundMembersAsync(userId.Value, id, ct);
            return members is null ? Results.NotFound() : Results.Ok(members);
        });

        // Guarantor-authored "How It Works" content shown to this fund's members.
        group.MapPut("/{id:guid}/how-it-works", async (
            Guid id,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundService fundService,
            [FromBody] UpdateHowItWorksRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var fund = await fundService.UpdateFundAsync(
                    userId.Value, id, f => f.SetHowItWorks(request.Content), "Fund.HowItWorksUpdated", ct: ct);
                return fund is null ? Results.NotFound() : Results.Ok(FundDtoMapper.Map(fund));
            }
            catch (InvalidFundException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // Transition state for a Guarantor-owned fund (Family Capital readiness).
        // Family Contributions and Committed Capital are always reported as
        // separate pools. The transition itself never happens automatically.
        group.MapGet("/{id:guid}/transition-status", async (
            Guid id,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundTransitionService transitionService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var status = await transitionService.GetTransitionStatusAsync(userId.Value, id, ct);
            return status is null ? Results.NotFound() : Results.Ok(status);
        });

        // Manual, one-way transition to Family Capital. Eligibility is validated
        // server-side against confirmed Family Contributions; the client cannot
        // force an ineligible transition.
        group.MapPost("/{id:guid}/transition", async (
            Guid id,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundTransitionService transitionService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await transitionService.TransitionToFamilyCapitalAsync(userId.Value, id, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (InvalidFundException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        return app;
    }

    private const string Policy = "GuarantorOnly";

    private static Guid? UserId(HttpContext http, UserManager<ApplicationUser> userManager)
    {
        var raw = userManager.GetUserId(http.User);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

internal static class FundDtoMapper
{
    public static FundDto Map(Domain.Funds.Fund fund) => new()
    {
        Id = fund.Id,
        Name = fund.Name,
        Type = fund.Type,
        JoinCode = fund.JoinCode,
        CommittedCapital = fund.CommittedCapital,
        ContributionMultiplier = fund.ContributionMultiplier,
        InterestRate = fund.InterestRate,
        HowItWorks = fund.HowItWorks,
        Status = fund.Status,
        TransitionedAtUtc = fund.TransitionedAtUtc,
        CreatedAtUtc = fund.CreatedAtUtc,
    };
}
