using FamilyTrustFund.Application.Payments;
using FamilyTrustFund.Domain.Payments;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FamilyTrustFund.Api.Payments;

/// <summary>
/// Endpoints for the offline/manual loan disbursement workflow (ADR-056).
/// The Fund Originator reports an offline payment, uploads proof and submits it;
/// the member confirms or disputes receipt, and only confirmation disburses the
/// loan.
/// </summary>
public static class ManualDisbursementEndpoints
{
    public static IEndpointRouteBuilder MapManualDisbursementEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Guarantor endpoints ───────────────────────────────────────────

        var guarantorGroup = app.MapGroup("/api/guarantor/payments/disbursements/offline")
            .RequireAuthorization("GuarantorOnly");

        // Report an offline payment (Fund Originator). Creates a draft; the
        // ledger is untouched until the member confirms receipt.
        guarantorGroup.MapPost("/", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ManualDisbursementService service,
            [FromBody] CreateManualDisbursementRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var report = await service.CreateDraftAsync(userId.Value, request, ct);
                return Results.Created($"/api/guarantor/payments/disbursements/offline/{report.Id}", report);
            }
            catch (InvalidPaymentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // Submit a draft to the member for confirmation (Fund Originator).
        guarantorGroup.MapPost("/{reportId:guid}/submit", async (
            Guid reportId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ManualDisbursementService service,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(await service.SubmitAsync(userId.Value, reportId, ct));
            }
            catch (InvalidPaymentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // Cancel an offline report (Fund Originator). Returns the loan to APPROVED.
        guarantorGroup.MapPost("/{reportId:guid}/cancel", async (
            Guid reportId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ManualDisbursementService service,
            [FromBody] CancelManualDisbursementRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(await service.CancelAsync(userId.Value, reportId, request.Reason, ct));
            }
            catch (InvalidPaymentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // The latest offline report for a loan in the Guarantor's fund.
        guarantorGroup.MapGet("/loan/{loanId:guid}", async (
            Guid loanId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ManualDisbursementService service,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var report = await service.GetLatestForLoanAsync(loanId, userId.Value, ct);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        // ── Member endpoints ──────────────────────────────────────────────

        var memberGroup = app.MapGroup("/api/loans/disbursements/offline")
            .RequireAuthorization("MemberOnly");

        // Offline reports for the member's own loans.
        memberGroup.MapGet("/mine", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ManualDisbursementService service,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await service.GetMineAsync(userId.Value, ct));
        });

        // A single report the member owns (for evidence access).
        memberGroup.MapGet("/{reportId:guid}", async (
            Guid reportId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ManualDisbursementService service,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var report = await service.GetForOwnerAsync(reportId, userId.Value, ct);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        // The member confirms receipt — this disburses the loan.
        memberGroup.MapPost("/{reportId:guid}/confirm", async (
            Guid reportId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ManualDisbursementService service,
            [FromBody] ConfirmManualDisbursementRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(await service.ConfirmByMemberAsync(userId.Value, reportId, request.Note, ct));
            }
            catch (InvalidPaymentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // The member disputes receipt — the report stays live until the
        // Guarantor resolves it.
        memberGroup.MapPost("/{reportId:guid}/dispute", async (
            Guid reportId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ManualDisbursementService service,
            [FromBody] DisputeManualDisbursementRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(await service.DisputeByMemberAsync(userId.Value, reportId, request.Reason, ct));
            }
            catch (InvalidPaymentException ex)
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
