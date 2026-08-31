using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Application.Loans;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FamilyTrustFund.Api.Loans;

public static class LoanEndpoints
{
    public static IEndpointRouteBuilder MapLoanEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Member endpoints ──────────────────────────────────────────────

        var memberGroup = app.MapGroup("/api/loans")
            .RequireAuthorization("MemberOnly");

        // Request a loan (Member).
        memberGroup.MapPost("/request", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            LoanService loanService,
            [FromBody] RequestLoanRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var loan = await loanService.RequestLoanAsync(userId.Value, request, ct);
                return Results.Created($"/api/loans/{loan.Id}", loan);
            }
            catch (InvalidLoanException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // View own loans (Member).
        memberGroup.MapGet("/mine", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            LoanService loanService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var loans = await loanService.GetLoansByMemberAsync(userId.Value, ct);
            return Results.Ok(loans);
        });

        // Get lending capacity for a fund (Member).
        memberGroup.MapGet("/lending-capacity/{fundId:guid}", async (
            Guid fundId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            LoanService loanService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var capacity = await loanService.GetLendingCapacityAsync(fundId, userId.Value, ct);
                return Results.Ok(capacity);
            }
            catch (InvalidLoanException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // Get a specific loan by ID (Member — must own the loan or be in the same fund).
        memberGroup.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            LoanService loanService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            // Get member's loans and check if the requested loan is among them.
            var loans = await loanService.GetLoansByMemberAsync(userId.Value, ct);
            var loan = loans.FirstOrDefault(l => l.Id == id);
            return loan is null ? Results.NotFound() : Results.Ok(loan);
        });

        // ── Guarantor endpoints ───────────────────────────────────────────

        var guarantorGroup = app.MapGroup("/api/guarantor/loans")
            .RequireAuthorization("GuarantorOnly");

        // Pending loan request queue (Guarantor).
        guarantorGroup.MapGet("/pending", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            LoanService loanService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var loans = await loanService.GetPendingRequestsForGuarantorAsync(userId.Value, ct);
            return Results.Ok(loans);
        });

        // All loans for a fund (Guarantor — ownership-scoped).
        guarantorGroup.MapGet("/fund/{fundId:guid}", async (
            Guid fundId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundService fundService,
            LoanService loanService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            // Verify the fund belongs to this Guarantor.
            var fund = await fundService.GetFundForGuarantorAsync(userId.Value, fundId, ct);
            if (fund is null)
            {
                return Results.NotFound();
            }

            var loans = await loanService.GetLoansByFundAsync(fundId, ct);
            return Results.Ok(loans);
        });

        // Approve a loan (Guarantor).
        guarantorGroup.MapPost("/approve", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            LoanService loanService,
            [FromBody] ApproveLoanRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var loan = await loanService.ApproveLoanAsync(userId.Value, request, ct);
                return Results.Ok(loan);
            }
            catch (InvalidLoanException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // Reject a loan (Guarantor).
        guarantorGroup.MapPost("/reject", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            LoanService loanService,
            [FromBody] RejectLoanRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var loan = await loanService.RejectLoanAsync(userId.Value, request, ct);
                return Results.Ok(loan);
            }
            catch (InvalidLoanException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // Get lending capacity for a fund (Guarantor — ownership-scoped).
        guarantorGroup.MapGet("/lending-capacity/{fundId:guid}", async (
            Guid fundId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            FundService fundService,
            LoanService loanService,
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

            var capacity = await loanService.GetLendingCapacityAsync(fundId, ct: ct);
            return Results.Ok(capacity);
        });

        return app;
    }

    private static Guid? UserId(HttpContext http, UserManager<ApplicationUser> userManager)
    {
        var raw = userManager.GetUserId(http.User);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
