using FamilyTrustFund.Application.Payments;
using FamilyTrustFund.Domain.Payments;
using Microsoft.AspNetCore.Mvc;

namespace FamilyTrustFund.Api.Payments;

/// <summary>
/// Development-only disbursement verification. Lets a developer finalize a
/// pending disbursement before the provider webhook is reachable from a local
/// machine (Paystack cannot POST to localhost). Reuses the same idempotent
/// webhook pipeline, so behaviour matches production. Never mapped outside
/// development (see Program.cs).
/// </summary>
public static class DevPaymentEndpoints
{
    public sealed record VerifyDisbursementRequest
    {
        /// <summary>The provider transfer reference recorded at initiation (e.g. TRF_...).</summary>
        public string ProviderReference { get; init; } = string.Empty;

        /// <summary>Desired outcome. Defaults to Successful.</summary>
        public DisbursementStatus Status { get; init; } = DisbursementStatus.Successful;
    }

    public static IEndpointRouteBuilder MapDevPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dev/payments");

        group.MapPost("/disbursement/verify", async (
            [FromBody] VerifyDisbursementRequest request,
            DisbursementService disbursementService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ProviderReference))
            {
                return Results.BadRequest(new { message = "Provider reference is required." });
            }

            try
            {
                var dto = await disbursementService.ProcessProviderWebhookAsync(
                    DisbursementService.DefaultProvider,
                    eventId: $"dev-verify-{Guid.NewGuid():N}",
                    providerReference: request.ProviderReference.Trim(),
                    status: request.Status,
                    detail: "Manually verified (development)",
                    ct: ct);

                return dto is null
                    ? Results.NotFound(new { message = "No pending disbursement matched; it may already be finalised." })
                    : Results.Ok(dto);
            }
            catch (InvalidPaymentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        return app;
    }
}
