using FamilyTrustFund.Application.Payments;
using FamilyTrustFund.Domain.Payments;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FamilyTrustFund.Api.Payments;

/// <summary>
/// Guarantor endpoints for per-loan capital payments ("pay per disbursement",
/// ADR-044). A Guarantor does not pre-fund the platform: they pay the gross
/// amount (approved loan amount + provider fee) at the moment a loan is
/// disbursed, and the member is settled through their subaccount once the
/// payment is confirmed. These endpoints expose the estimate, verification and
/// history of those payments; initiation happens as part of the disbursement
/// flow.
/// </summary>
public static class CapitalFundingEndpoints
{
    public static IEndpointRouteBuilder MapCapitalFundingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/guarantor/funds/{fundId:guid}/capital")
            .RequireAuthorization("GuarantorOnly");

        // Server-authoritative summary of per-loan capital payments for the fund.
        group.MapGet("/", async (
            Guid fundId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            CapitalFundingService capitalFundingService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var summary = await capitalFundingService.GetSummaryAsync(userId.Value, fundId, ct);
                return Results.Ok(summary);
            }
            catch (InvalidPaymentException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
        });

        // Gross amount the Guarantor must pay so the net reaches the target.
        group.MapPost("/estimate", async (
            Guid fundId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            CapitalFundingService capitalFundingService,
            [FromBody] CapitalFundingEstimateRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var estimate = await capitalFundingService.EstimateAsync(userId.Value, fundId, request, ct);
                return Results.Ok(estimate);
            }
            catch (InvalidPaymentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // Verify a pending capital payment after the Guarantor completes the checkout.
        group.MapPost("/payments/{providerReference}/verify", async (
            Guid fundId,
            string providerReference,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            CapitalFundingService capitalFundingService,
            DisbursementService disbursementService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var transaction = await capitalFundingService.VerifyAsync(userId.Value, fundId, providerReference, ct);

                // The collection may belong to a loan disbursement. Once the
                // Guarantor's payment is confirmed, finalise the disbursement so
                // the member is settled through their subaccount (idempotent).
                if (transaction.Status == CapitalTransactionStatus.Confirmed)
                {
                    await disbursementService.ProcessProviderWebhookAsync(
                        CapitalFundingService.DefaultProvider,
                        eventId: $"verify-{providerReference}",
                        providerReference: providerReference,
                        status: DisbursementStatus.Successful,
                        detail: "Verified after checkout (Guarantor payment confirmed)",
                        ct: ct);
                }

                return Results.Ok(transaction);
            }
            catch (InvalidPaymentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // History of capital payments for the fund (most recent first).
        group.MapGet("/payments", async (
            Guid fundId,
            int page,
            int pageSize,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            CapitalFundingService capitalFundingService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await capitalFundingService.GetTransactionsAsync(
                    userId.Value, fundId, page <= 0 ? 1 : page, pageSize <= 0 ? 10 : pageSize, ct);
                return Results.Ok(result);
            }
            catch (InvalidPaymentException ex)
            {
                return Results.NotFound(new { message = ex.Message });
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