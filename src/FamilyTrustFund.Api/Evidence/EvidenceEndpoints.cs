using FamilyTrustFund.Application.Contributions;
using FamilyTrustFund.Application.Evidence;
using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Application.Repayments;
using FamilyTrustFund.Domain.Contributions;
using FamilyTrustFund.Domain.Evidence;
using FamilyTrustFund.Domain.Repayments;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FamilyTrustFund.Api.Evidence;

public static class EvidenceEndpoints
{
    private const int MaxUploadBytes = 5 * 1024 * 1024; // 5 MB

    public static IEndpointRouteBuilder MapEvidenceEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Member endpoints ──────────────────────────────────────────────

        var memberGroup = app.MapGroup("/api/evidence")
            .RequireAuthorization("MemberOnly");

        // Upload evidence against the member's own pending contribution.
        memberGroup.MapPost("/contribution/{contributionId:guid}", async (
            Guid contributionId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ContributionService contributionService,
            EvidenceService evidenceService,
            IFormFile? file,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { message = "A file is required." });
            }

            if (file.Length > MaxUploadBytes)
            {
                return Results.BadRequest(new { message = "Evidence file is too large. Maximum size is 5 MB." });
            }

            var contribution = await contributionService.GetContributionForOwnerAsync(contributionId, userId.Value, ct);
            if (contribution is null)
            {
                return Results.NotFound(new { message = "Contribution not found or you are not its owner." });
            }

            if (contribution.Status != ContributionStatus.PendingConfirmation)
            {
                return Results.BadRequest(new { message = "Evidence can only be added while the contribution is pending confirmation." });
            }

            try
            {
                using var memory = new MemoryStream();
                await file.CopyToAsync(memory, ct);
                var evidence = await evidenceService.UploadAsync(
                    userId.Value,
                    "Contribution",
                    contributionId,
                    file.FileName,
                    file.ContentType,
                    memory.ToArray(),
                    ct);
                return Results.Created($"/api/evidence/{evidence.Id}", evidence);
            }
            catch (InvalidEvidenceException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // Upload evidence against the member's own pending repayment.
        memberGroup.MapPost("/repayment/{pendingRepaymentId:guid}", async (
            Guid pendingRepaymentId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            PendingRepaymentService pendingRepaymentService,
            EvidenceService evidenceService,
            IFormFile? file,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { message = "A file is required." });
            }

            if (file.Length > MaxUploadBytes)
            {
                return Results.BadRequest(new { message = "Evidence file is too large. Maximum size is 5 MB." });
            }

            var pending = await pendingRepaymentService.GetForOwnerAsync(pendingRepaymentId, userId.Value, ct);
            if (pending is null)
            {
                return Results.NotFound(new { message = "Pending repayment not found or you are not its owner." });
            }

            if (pending.Status != PendingRepaymentStatus.PendingConfirmation)
            {
                return Results.BadRequest(new { message = "Evidence can only be added while the repayment is pending confirmation." });
            }

            try
            {
                using var memory = new MemoryStream();
                await file.CopyToAsync(memory, ct);
                var evidence = await evidenceService.UploadAsync(
                    userId.Value,
                    "Repayment",
                    pendingRepaymentId,
                    file.FileName,
                    file.ContentType,
                    memory.ToArray(),
                    ct);
                return Results.Created($"/api/evidence/{evidence.Id}", evidence);
            }
            catch (InvalidEvidenceException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // My evidence across my resources (Member).
        memberGroup.MapGet("/mine", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            EvidenceService evidenceService,
            ContributionService contributionService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            // Only expose evidence the member owns (uploaded by them).
            var mine = await evidenceService.GetMineAsync(userId.Value, ct);
            return Results.Ok(mine);
        });

        // Download a specific evidence file.
        memberGroup.MapGet("/{evidenceId:guid}", async (
            Guid evidenceId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            EvidenceService evidenceService,
            ContributionService contributionService,
            PendingRepaymentService pendingRepaymentService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            return await DownloadOrAuthorizeAsync(
                evidenceId, userId.Value, isMember: true, http, contributionService, pendingRepaymentService, evidenceService, ct);
        });

        // ── Guarantor endpoints ───────────────────────────────────────────

        var guarantorGroup = app.MapGroup("/api/guarantor/evidence")
            .RequireAuthorization("GuarantorOnly");

        // Evidence for a contribution in the Guarantor's fund.
        guarantorGroup.MapGet("/contribution/{contributionId:guid}", async (
            Guid contributionId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ContributionService contributionService,
            EvidenceService evidenceService,
            FundService fundService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var contribution = await contributionService.GetContributionForGuarantorAsync(contributionId, userId.Value, ct);
            if (contribution is null)
            {
                return Results.NotFound(new { message = "Contribution not found or not in your fund." });
            }

            var evidence = await evidenceService.GetForResourceAsync("Contribution", contributionId, ct);
            return Results.Ok(evidence);
        });

        // Evidence for a pending repayment in the Guarantor's fund.
        guarantorGroup.MapGet("/repayment/{pendingRepaymentId:guid}", async (
            Guid pendingRepaymentId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            PendingRepaymentService pendingRepaymentService,
            EvidenceService evidenceService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var pending = await pendingRepaymentService.GetForGuarantorAsync(pendingRepaymentId, userId.Value, ct);
            if (pending is null)
            {
                return Results.NotFound(new { message = "Pending repayment not found or not in your fund." });
            }

            var evidence = await evidenceService.GetForResourceAsync("Repayment", pendingRepaymentId, ct);
            return Results.Ok(evidence);
        });

        // Download evidence by id (Guarantor — must own the resource's fund).
        guarantorGroup.MapGet("/{evidenceId:guid}", async (
            Guid evidenceId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            EvidenceService evidenceService,
            ContributionService contributionService,
            PendingRepaymentService pendingRepaymentService,
            CancellationToken ct) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            return await DownloadOrAuthorizeAsync(
                evidenceId, userId.Value, isMember: false, http, contributionService, pendingRepaymentService, evidenceService, ct);
        });

        return app;
    }

    private static async Task<IResult> DownloadOrAuthorizeAsync(
        Guid evidenceId,
        Guid userId,
        bool isMember,
        HttpContext http,
        ContributionService contributionService,
        PendingRepaymentService pendingRepaymentService,
        EvidenceService evidenceService,
        CancellationToken ct)
    {
        var result = await evidenceService.ReadAsync(evidenceId, ct);
        if (result is null)
        {
            return Results.NotFound(new { message = "Evidence not found." });
        }

        var (evidence, storage) = result.Value;

        // Authorization: resolve the owning resource and enforce rules.
        var allowed = await IsAllowedToViewAsync(
            evidence, userId, isMember, contributionService, pendingRepaymentService, ct);
        if (!allowed)
        {
            return Results.Forbid();
        }

        var bytes = storage.Content ?? Array.Empty<byte>();
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        http.Response.Headers["Content-Disposition"] =
            $"inline; filename=\"{Uri.EscapeDataString(evidence.OriginalFileName)}\"";
        return Results.File(bytes, evidence.ContentType, evidence.OriginalFileName);
    }

    /// <summary>
    /// Enforces AGENTS §9 evidence access: a member can only view their own
    /// evidence; a Guarantor can view evidence for resources in their funds.
    /// </summary>
    private static async Task<bool> IsAllowedToViewAsync(
        Domain.Evidence.PaymentEvidence evidence,
        Guid userId,
        bool isMember,
        ContributionService contributionService,
        PendingRepaymentService pendingRepaymentService,
        CancellationToken ct)
    {
        if (string.Equals(evidence.ResourceType, "Contribution", StringComparison.OrdinalIgnoreCase))
        {
            if (isMember)
            {
                // The owner of the contribution may view its evidence.
                var contribution = await contributionService.GetContributionForOwnerAsync(evidence.ResourceId, userId, ct);
                return contribution is not null;
            }

            // Guarantor must own the fund the contribution belongs to.
            var contributionForGuarantor =
                await contributionService.GetContributionForGuarantorAsync(evidence.ResourceId, userId, ct);
            return contributionForGuarantor is not null;
        }

        if (string.Equals(evidence.ResourceType, "Repayment", StringComparison.OrdinalIgnoreCase))
        {
            if (isMember)
            {
                // The owner of the pending repayment may view its evidence.
                var pendingForOwner = await pendingRepaymentService.GetForOwnerAsync(evidence.ResourceId, userId, ct);
                return pendingForOwner is not null;
            }

            // Guarantor must own the fund the repayment belongs to.
            var pendingForGuarantor = await pendingRepaymentService.GetForGuarantorAsync(evidence.ResourceId, userId, ct);
            return pendingForGuarantor is not null;
        }

        return false;
    }

    private static Guid? UserId(HttpContext http, UserManager<ApplicationUser> userManager)
    {
        var raw = userManager.GetUserId(http.User);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
