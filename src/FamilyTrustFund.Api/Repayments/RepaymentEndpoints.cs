using FamilyTrustFund.Application.Loans;
using FamilyTrustFund.Application.Repayments;
using FamilyTrustFund.Domain.Repayments;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FamilyTrustFund.Api.Repayments;

public static class RepaymentEndpoints
{
    public static IEndpointRouteBuilder MapRepaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var memberGroup = app.MapGroup("/api/loans")
            .RequireAuthorization("MemberOnly");

        // View current repayment schedule for one of the member's loans.
        memberGroup.MapGet("/{loanId:guid}/repayments/schedule", async (
            Guid loanId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ILoanRepository loanRepository,
            RepaymentService repaymentService,
            CancellationToken ct) =>
        {
            if (!await IsOwnerAsync(http, userManager, loanRepository, loanId, ct))
            {
                return Results.NotFound();
            }

            try
            {
                var items = await repaymentService.GetCurrentScheduleItemsAsync(loanId, ct);
                return Results.Ok(items);
            }
            catch (InvalidRepaymentException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
        });

        // View repayment summary for one of the member's loans.
        memberGroup.MapGet("/{loanId:guid}/repayments/summary", async (
            Guid loanId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ILoanRepository loanRepository,
            RepaymentService repaymentService,
            CancellationToken ct) =>
        {
            if (!await IsOwnerAsync(http, userManager, loanRepository, loanId, ct))
            {
                return Results.NotFound();
            }

            var summary = await repaymentService.GetSummaryAsync(loanId, ct);
            return Results.Ok(summary);
        });

        // View repayment history for one of the member's loans.
        memberGroup.MapGet("/{loanId:guid}/repayments/history", async (
            Guid loanId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            ILoanRepository loanRepository,
            RepaymentService repaymentService,
            CancellationToken ct) =>
        {
            if (!await IsOwnerAsync(http, userManager, loanRepository, loanId, ct))
            {
                return Results.NotFound();
            }

            var history = await repaymentService.GetRepaymentsAsync(loanId, ct);
            return Results.Ok(history);
        });

        // All repayment history for the member across every loan (paginated).
        // Shown even when the member has no currently active loan.
        memberGroup.MapGet("/repayments/history/mine", async (
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            RepaymentService repaymentService,
            CancellationToken ct,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10) =>
        {
            var userId = UserId(http, userManager);
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var result = await repaymentService.GetMyRepaymentsAsync(userId.Value, page, pageSize, ct);
            return Results.Ok(result);
        });

        return app;
    }

    /// <summary>
    /// Returns true if the current user is the owner of the loan (member).
    /// </summary>
    private static async Task<bool> IsOwnerAsync(
        HttpContext http,
        UserManager<ApplicationUser> userManager,
        ILoanRepository loanRepository,
        Guid loanId,
        CancellationToken ct)
    {
        var userId = UserId(http, userManager);
        if (userId is null)
        {
            return false;
        }

        var loan = await loanRepository.GetByIdAsync(loanId, ct);
        return loan is not null && loan.MemberId == userId.Value;
    }

    private static Guid? UserId(HttpContext http, UserManager<ApplicationUser> userManager)
    {
        var raw = userManager.GetUserId(http.User);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
