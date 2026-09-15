using FamilyTrustFund.Application.Loans;
using FamilyTrustFund.Application.Repayments;
using FamilyTrustFund.Domain.Repayments;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FamilyTrustFund.Api.Repayments;

public static class PendingRepaymentEndpoints
{
    public static IEndpointRouteBuilder MapPendingRepaymentEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Member endpoints ──────────────────────────────────────────────

        var memberGroup = app.MapGroup("/api/loans")
            .RequireAuthorization("MemberOnly");

        // Declare a manual repayment (Member). It stays pending until the
        // Guarantor confirms; only confirmation posts to the ledger.
        memberGroup.MapPost("/{loanId:guid}/repayments/declare", async (
            Guid loanId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            PendingRepaymentService pendingRepaymentService,
            [FromBody] ReportPendingRepaymentRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            request = Normalise(request, loanId);
            if (request is null)
            {
                return Results.BadRequest(new { message = "Loan id mismatch." });
            }

            try
            {
                var pending = await pendingRepaymentService.ReportAsync(userId.Value, request, ct);
                return Results.Created($"/api/pending-repayments/{pending.Id}", pending);
            }
            catch (InvalidRepaymentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // My pending repayments across loans (Member).
        memberGroup.MapGet("/repayments/pending/mine", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            PendingRepaymentService pendingRepaymentService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var items = await pendingRepaymentService.GetMineAsync(userId.Value, ct);
            return Results.Ok(items);
        });

        // A single pending repayment the member owns (for evidence access).
        memberGroup.MapGet("/repayments/pending/{pendingId:guid}", async (
            Guid pendingId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            PendingRepaymentService pendingRepaymentService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var item = await pendingRepaymentService.GetForOwnerAsync(pendingId, userId.Value, ct);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        // ── Guarantor endpoints ───────────────────────────────────────────

        var guarantorGroup = app.MapGroup("/api/guarantor/repayments")
            .RequireAuthorization("GuarantorOnly");

        // Pending repayment confirmation queue (Guarantor).
        guarantorGroup.MapGet("/pending", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            PendingRepaymentService pendingRepaymentService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var items = await pendingRepaymentService.GetPendingForGuarantorAsync(userId.Value, ct);
            return Results.Ok(items);
        });

        // Confirm a pending repayment (Guarantor) — posts the financial transaction.
        guarantorGroup.MapPost("/confirm", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            PendingRepaymentService pendingRepaymentService,
            [FromBody] ConfirmPendingRepaymentRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await pendingRepaymentService.ConfirmAsync(userId.Value, request, ct);
                return Results.Ok(result);
            }
            catch (InvalidRepaymentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // Reject a pending repayment (Guarantor) — never posts to the ledger.
        guarantorGroup.MapPost("/reject", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            PendingRepaymentService pendingRepaymentService,
            [FromBody] RejectPendingRepaymentRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var item = await pendingRepaymentService.RejectAsync(userId.Value, request, ct);
                return Results.Ok(item);
            }
            catch (InvalidRepaymentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        return app;
    }

    private static ReportPendingRepaymentRequest? Normalise(ReportPendingRepaymentRequest request, Guid loanId)
    {
        if (request.LoanId == Guid.Empty)
        {
            return new ReportPendingRepaymentRequest
            {
                LoanId = loanId,
                Amount = request.Amount,
                Kind = request.Kind,
                Reference = request.Reference,
                Note = request.Note,
            };
        }

        return request.LoanId == loanId ? request : null;
    }

    private static Guid? UserId(HttpContext http, UserManager<ApplicationUser> userManager)
    {
        var raw = userManager.GetUserId(http.User);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
