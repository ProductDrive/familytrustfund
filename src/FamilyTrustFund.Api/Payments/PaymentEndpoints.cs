using System.Text;
using System.Text.Json;
using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Application.Loans;
using FamilyTrustFund.Application.Payments;
using FamilyTrustFund.Domain.Payments;
using FamilyTrustFund.Infrastructure.Identity;
using FamilyTrustFund.Infrastructure.Payments;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FamilyTrustFund.Api.Payments;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Member endpoints ──────────────────────────────────────────────
        var memberGroup = app.MapGroup("/api/payments")
            .RequireAuthorization("MemberOnly");

        memberGroup.MapPost("/recipient", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            DisbursementService disbursementService,
            [FromBody] SaveRecipientRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var recipient = await disbursementService.SaveRecipientAsync(userId.Value, request, ct: ct);
                return Results.Ok(recipient);
            }
            catch (InvalidPaymentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        memberGroup.MapPost("/recipient/verify", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
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
                var recipient = await disbursementService.VerifyRecipientAsync(userId.Value, ct);
                return Results.Ok(recipient);
            }
            catch (InvalidPaymentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        memberGroup.MapGet("/recipient", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            DisbursementService disbursementService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var recipient = await disbursementService.GetRecipientForMemberAsync(userId.Value, ct);
            return recipient is null ? Results.NotFound() : Results.Ok(recipient);
        });

        // Get disbursement status for one of the member's loans.
        memberGroup.MapGet("/disbursement/{loanId:guid}", async (
            Guid loanId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            LoanService loanService,
            DisbursementService disbursementService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var own = (await loanService.GetLoansByMemberAsync(userId.Value, ct)).FirstOrDefault(l => l.Id == loanId);
            if (own is null)
            {
                return Results.NotFound();
            }

            var txn = await disbursementService.GetDisbursementForLoanAsync(loanId, ct);
            return txn is null ? Results.NotFound() : Results.Ok(txn);
        });

        // ── Guarantor endpoints ───────────────────────────────────────────
        var guarantorGroup = app.MapGroup("/api/guarantor/payments")
            .RequireAuthorization("GuarantorOnly");

        // Initiate disbursement of an approved loan (Guarantor, ownership-scoped).
        guarantorGroup.MapPost("/disburse", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            DisbursementService disbursementService,
            [FromBody] InitiateDisbursementRequest request,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var disbursement = await disbursementService.InitiateDisbursementAsync(userId.Value, request, ct);
                return Results.Ok(disbursement);
            }
            catch (InvalidPaymentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // Disbursement status for a loan in a Guarantor's fund (ownership-scoped).
        guarantorGroup.MapGet("/disbursement/{loanId:guid}", async (
            Guid loanId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            LoanService loanService,
            FundService fundService,
            DisbursementService disbursementService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var loan = await loanService.GetLoanByIdForGuarantorAsync(userId.Value, loanId, ct);
            if (loan is null)
            {
                return Results.NotFound();
            }

            var txn = await disbursementService.GetDisbursementForLoanAsync(loanId, ct);
            return txn is null ? Results.NotFound() : Results.Ok(txn);
        });

        // ── Provider webhook (unauthenticated; signature-verified) ────────
        app.MapPost("/api/webhooks/paystack", async (
            HttpRequest request,
            IPaymentProviderRegistry providers,
            DisbursementService disbursementService,
            CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            var raw = await reader.ReadToEndAsync(ct);

            var signature = request.Headers["x-paystack-signature"].ToString();
            var provider = providers.Get("Paystack") as PaystackPaymentProvider;
            if (provider is null || !provider.VerifyWebhookSignature(raw, signature))
            {
                return Results.Unauthorized();
            }

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var eventName = doc.RootElement.TryGetProperty("event", out var ev) ? ev.GetString() : null;
                var data = doc.RootElement.TryGetProperty("data", out var d) ? d : default;
                var eventId = data.TryGetProperty("id", out var id) ? id.GetString() : null;
                var reference = GetReference(data);

                if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(reference))
                {
                    return Results.BadRequest(new { message = "Missing event id or reference." });
                }

                var status = MapTransferStatus(eventName);
                if (status is null)
                {
                    // Unrelated/unknown event; acknowledge so the provider doesn't retry pointlessly.
                    return Results.Ok(new { status = "ignored" });
                }

                var result = await disbursementService.ProcessProviderWebhookAsync(
                    "Paystack", eventId, reference, status.Value, ct: ct);

                return result is null
                    ? Results.Ok(new { status = "duplicate" })
                    : Results.Ok(new { status = "processed" });
            }
            catch (InvalidPaymentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { message = "Invalid payload." });
            }
        });

        return app;
    }

    private static DisbursementStatus? MapTransferStatus(string? eventName) => eventName switch
    {
        "transfer.success" => DisbursementStatus.Successful,
        "transfer.failed" => DisbursementStatus.Failed,
        "transfer.reversed" => DisbursementStatus.Reversed,
        _ => null,
    };

    private static string? GetReference(JsonElement data)
    {
        if (data.TryGetProperty("reference", out var r) && r.ValueKind == JsonValueKind.String)
        {
            return r.GetString();
        }

        if (data.TryGetProperty("transfer_code", out var t) && t.ValueKind == JsonValueKind.String)
        {
            return t.GetString();
        }

        return null;
    }

    private static Guid? UserId(HttpContext http, UserManager<ApplicationUser> userManager)
    {
        var raw = userManager.GetUserId(http.User);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
