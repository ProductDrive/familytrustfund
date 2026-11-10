using FamilyTrustFund.Api.Authorization;
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

        // Update repayment account details for a Guarantor-owned fund.
        group.MapPut("/{id:guid}/repayment-account", async (
            Guid id,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundService fundService,
            [FromBody] UpdateRepaymentAccountRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var fund = await fundService.UpdateRepaymentAccountAsync(userId.Value, id, request, ct);
                return fund is null ? Results.NotFound() : Results.Ok(FundDtoMapper.Map(fund));
            }
            catch (InvalidFundException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // Get fund details for members (includes repayment account if set).
        group.MapGet("/{id:guid}/member", async (
            Guid id,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundService fundService,
            Application.Membership.MembershipService membershipService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var fund = await fundService.GetFundByIdAsync(id, ct);
            if (fund is null)
            {
                return Results.NotFound();
            }

            var member = await membershipService.GetMembershipAsync(userId.Value, id, ct);
            if (member is null || member.Status != Domain.Membership.MemberStatus.Active)
            {
                return Results.Forbid();
            }

            return Results.Ok(FundDtoMapper.Map(fund));
        }).RequireAuthorization(ApiPolicies.MemberOnly);

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

        // ── Group Guarantors (ADR-051) ────────────────────────────────────
        //
        // A group fund is operated by its Fund Originator plus every active
        // co-guarantor. Membership changes are blocked by the service while a
        // loan is awaiting decisions, so the approver set cannot shift mid-vote.

        // Guarantors of a fund (originator or active co-guarantor).
        group.MapGet("/{id:guid}/guarantors", async (
            Guid id,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundGuarantorService guarantorService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var guarantors = await guarantorService.GetFundGuarantorsAsync(userId.Value, id, ct);
            return guarantors is null ? Results.NotFound() : Results.Ok(guarantors);
        });

        // Invite an existing, approved Guarantor account (originator only).
        group.MapPost("/{id:guid}/guarantors/invitations", async (
            Guid id,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundGuarantorService guarantorService,
            [FromBody] InviteGuarantorRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var invitation = await guarantorService.InviteAsync(userId.Value, id, request, ct);
                return Results.Created(
                    $"/api/funds/{id}/guarantors/{invitation.GuarantorId}", invitation);
            }
            catch (InvalidFundGuarantorException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // Withdraw an invitation or remove an active co-guarantor (originator only).
        group.MapDelete("/{id:guid}/guarantors/{guarantorId:guid}", async (
            Guid id,
            Guid guarantorId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundGuarantorService guarantorService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var removed = await guarantorService.RevokeAsync(userId.Value, id, guarantorId, ct);
                return Results.Ok(removed);
            }
            catch (InvalidFundGuarantorException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // Leave a group fund voluntarily (active co-guarantor only).
        group.MapPost("/{id:guid}/guarantors/leave", async (
            Guid id,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundGuarantorService guarantorService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                await guarantorService.LeaveAsync(userId.Value, id, ct);
                return Results.NoContent();
            }
            catch (InvalidFundGuarantorException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // Change the caller's own committed capital in a group fund.
        group.MapPut("/{id:guid}/guarantors/me/commitment", async (
            Guid id,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundGuarantorService guarantorService,
            [FromBody] UpdateGuarantorCommitmentRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var updated = await guarantorService.UpdateCommitmentAsync(
                    userId.Value, id, request, ct);
                return Results.Ok(updated);
            }
            catch (InvalidFundGuarantorException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // ── The caller's own invitations and group memberships ────────────

        var invitationGroup = app.MapGroup("/api/guarantor/invitations")
            .RequireAuthorization(Policy);

        invitationGroup.MapGet("/", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundGuarantorService guarantorService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var invitations = await guarantorService.GetMineAsync(userId.Value, ct);
            return Results.Ok(invitations);
        });

        invitationGroup.MapPost("/{membershipId:guid}/accept", async (
            Guid membershipId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundGuarantorService guarantorService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var accepted = await guarantorService.AcceptAsync(userId.Value, membershipId, ct);
                return Results.Ok(accepted);
            }
            catch (InvalidFundGuarantorException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        invitationGroup.MapPost("/{membershipId:guid}/decline", async (
            Guid membershipId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundGuarantorService guarantorService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var declined = await guarantorService.DeclineAsync(userId.Value, membershipId, ct);
                return Results.Ok(declined);
            }
            catch (InvalidFundGuarantorException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        return app;
    }

    private const string Policy = ApiPolicies.GuarantorOnly;

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
        GuarantorId = fund.GuarantorId,
        GuarantorMode = fund.GuarantorMode,
        ContributionMultiplier = fund.ContributionMultiplier,
        InterestRate = fund.InterestRate,
        MinLoanTermMonths = fund.MinLoanTermMonths,
        MaxLoanTermMonths = fund.MaxLoanTermMonths,
        HowItWorks = fund.HowItWorks,
        RepaymentBankName = fund.RepaymentBankName,
        RepaymentAccountNumber = fund.RepaymentAccountNumber,
        RepaymentAccountName = fund.RepaymentAccountName,
        Status = fund.Status,
        TransitionedAtUtc = fund.TransitionedAtUtc,
        CreatedAtUtc = fund.CreatedAtUtc,
    };
}
