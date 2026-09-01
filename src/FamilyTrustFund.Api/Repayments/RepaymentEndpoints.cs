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

        // Record a scheduled instalment payment (Member).
        memberGroup.MapPost("/{loanId:guid}/repayments", async (
            Guid loanId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            RepaymentService repaymentService,
            [FromBody] MakeRepaymentRequest request,
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
                var summary = await repaymentService.MakePaymentAsync(
                    userId.Value, request, RepaymentKind.Scheduled, ct);
                return Results.Ok(summary);
            }
            catch (InvalidRepaymentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // Record a lump-sum payment (Member).
        memberGroup.MapPost("/{loanId:guid}/repayments/lump-sum", async (
            Guid loanId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            RepaymentService repaymentService,
            [FromBody] MakeRepaymentRequest request,
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
                var summary = await repaymentService.MakePaymentAsync(
                    userId.Value, request, RepaymentKind.LumpSum, ct);
                return Results.Ok(summary);
            }
            catch (InvalidRepaymentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // Fully settle a loan (Member).
        memberGroup.MapPost("/{loanId:guid}/repayments/settle", async (
            Guid loanId,
            HttpContext http,
            UserManager<ApplicationUser> userManager,
            RepaymentService repaymentService,
            [FromBody] MakeRepaymentRequest request,
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
                var summary = await repaymentService.SettleAsync(userId.Value, request, ct);
                return Results.Ok(summary);
            }
            catch (InvalidRepaymentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        return app;
    }

    /// <summary>
    /// Binds the body request to the loan in the route, if the client omitted it.
    /// </summary>
    private static MakeRepaymentRequest? Normalise(MakeRepaymentRequest request, Guid loanId)
    {
        if (request.LoanId == Guid.Empty)
        {
            return new MakeRepaymentRequest
            {
                LoanId = loanId,
                Amount = request.Amount,
                Note = request.Note,
            };
        }

        return request.LoanId == loanId ? request : null;
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
