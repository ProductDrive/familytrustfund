using FamilyTrustFund.Application.Audit;
using FamilyTrustFund.Application.Loans;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Repayments;

namespace FamilyTrustFund.Application.Repayments;

/// <summary>
/// Server-authoritative repayment operations: schedule generation, scheduled
/// instalments, lump-sum recalculation, full settlement, surplus and overdue.
/// All financial state changes are committed transactionally (AGENTS §7).
/// </summary>
public class RepaymentService
{
    private readonly IRepaymentRepository _repaymentRepository;
    private readonly IAuditLog _auditLog;

    public RepaymentService(
        IRepaymentRepository repaymentRepository,
        IAuditLog auditLog)
    {
        _repaymentRepository = repaymentRepository;
        _auditLog = auditLog;
    }

    /// <summary>
    /// Creates the first (v1) repayment schedule for a disbursed loan using its
    /// frozen approved terms. Idempotent.
    /// </summary>
    public async Task<ScheduleDto> EnsureScheduleAsync(
        Guid actorId,
        Guid loanId,
        CancellationToken ct = default)
    {
        var loan = await _repaymentRepository.GetLoanAsync(loanId, ct)
            ?? throw new InvalidRepaymentException("Loan not found.");

        if (loan.Status != LoanStatus.Disbursed)
        {
            throw new InvalidRepaymentException("A repayment schedule can only be created for a disbursed loan.");
        }

        var existing = await _repaymentRepository.GetCurrentScheduleAsync(loanId, ct);
        if (existing is not null)
        {
            return ToScheduleDto(existing, isCurrent: true);
        }

        var schedule = LoanSchedule.CreateV1(
            loanId,
            loan.ApprovedAmount!.Value,
            loan.TotalRepayable,
            loan.ApprovedFrequency!.Value,
            loan.RepaymentTerm);

        // The unique (LoanId, Version) index is the authoritative guard: the
        // schedule creation is idempotent, so a concurrent verification/webhook
        // that already created this revision does not surface a duplicate-key
        // error (AGENTS §7.3).
        var saved = await _repaymentRepository.SaveNewScheduleAsync(schedule, ct);
        if (saved is null)
        {
            throw new InvalidRepaymentException("Repayment schedule could not be created.");
        }

        if (saved.Id == schedule.Id)
        {
            await _auditLog.RecordAsync(actorId, "Repayment.ScheduleCreated", "LoanSchedule", saved.Id,
                $"LoanId={loanId}, Version=1, Term={loan.RepaymentTerm}", ct);
            await _repaymentRepository.SaveChangesAsync(ct);
        }

        return ToScheduleDto(saved, isCurrent: true);
    }

    /// <summary>Returns the current schedule for a loan.</summary>
    public async Task<IReadOnlyList<ScheduleDto>> GetSchedulesAsync(
        Guid loanId,
        CancellationToken ct = default)
    {
        var schedules = await _repaymentRepository.GetAllSchedulesAsync(loanId, ct);
        return schedules
            .OrderBy(s => s.Version)
            .Select(s => ToScheduleDto(s, isCurrent: s.Version == schedules.Max(x => x.Version)))
            .ToList();
    }

    /// <summary>Returns current schedule items (for a member's active loan).</summary>
    public async Task<IReadOnlyList<ScheduleItemDto>> GetCurrentScheduleItemsAsync(
        Guid loanId,
        CancellationToken ct = default)
    {
        var schedule = await _repaymentRepository.GetCurrentScheduleAsync(loanId, ct)
            ?? throw new InvalidRepaymentException("No repayment schedule exists for this loan.");
        return schedule.Items
            .OrderBy(i => i.Sequence)
            .Select(ToItemDto)
            .ToList();
    }

    /// <summary>Returns repayments recorded against a loan.</summary>
    public async Task<IReadOnlyList<RepaymentDto>> GetRepaymentsAsync(
        Guid loanId,
        CancellationToken ct = default)
    {
        var repayments = await _repaymentRepository.GetRepaymentsAsync(loanId, ct);
        return repayments
            .OrderByDescending(r => r.PaidAtUtc)
            .Select(r => ToRepaymentDto(r))
            .ToList();
    }

    /// <summary>
    /// Returns the member's repayment history across all loans, paginated
    /// and including the fund name for each record.
    /// </summary>
    public async Task<PagedRepaymentsResult> GetMyRepaymentsAsync(
        Guid memberId,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 10;

        var (items, totalCount) = await _repaymentRepository.GetMyRepaymentsAsync(memberId, page, pageSize, ct);
        return new PagedRepaymentsResult
        {
            Items = items.Select(x => ToRepaymentDto(x.Repayment, x.FundName)).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <summary>Returns the server-calculated repayment summary for a loan.</summary>
    public async Task<RepaymentSummaryDto> GetSummaryAsync(
        Guid loanId,
        CancellationToken ct = default)
    {
        var loan = await _repaymentRepository.GetLoanAsync(loanId, ct)
            ?? throw new InvalidRepaymentException("Loan not found.");

        var schedule = await _repaymentRepository.GetCurrentScheduleAsync(loanId, ct);
        var repayments = await _repaymentRepository.GetRepaymentsAsync(loanId, ct);

        var today = DateTime.UtcNow.Date;
        var overdueItems = schedule?.Items
            .Where(i => i.Status != ScheduleItemStatus.Paid && i.DueDateUtc.Date < today)
            .ToList() ?? new List<LoanScheduleItem>();

        return new RepaymentSummaryDto
        {
            OutstandingBalance = loan.OutstandingBalance,
            TotalExpected = schedule?.Items.Sum(i => i.ExpectedAmount) ?? 0m,
            TotalPaid = repayments.Sum(r => r.ActualAmount),
            TotalSurplus = repayments.Sum(r => r.Surplus),
            OverdueItems = overdueItems.Count,
            OverdueAmount = overdueItems.Sum(i => i.ExpectedAmount - i.PaidAmount),
            SettlementQuote = loan.OutstandingBalance,
        };
    }

    /// <summary>
    /// Records a scheduled instalment (or, when kind is LumpSum, a lump-sum
    /// payment that recalculates the remaining schedule).
    /// </summary>
    public async Task<RepaymentSummaryDto> MakePaymentAsync(
        Guid memberId,
        MakeRepaymentRequest request,
        RepaymentKind kind,
        CancellationToken ct = default)
    {
        if (request.Amount <= 0)
        {
            throw new InvalidRepaymentException("Repayment amount must be greater than zero.");
        }

        var loan = await _repaymentRepository.GetLoanAsync(request.LoanId, ct)
            ?? throw new InvalidRepaymentException("Loan not found.");

        if (loan.MemberId != memberId)
        {
            throw new InvalidRepaymentException("A member can only repay their own loans.");
        }

        if (loan.Status != LoanStatus.Disbursed)
        {
            throw new InvalidRepaymentException("Only an active disbursed loan can be repaid.");
        }

        var schedule = await _repaymentRepository.GetCurrentScheduleAsync(request.LoanId, ct)
            ?? throw new InvalidRepaymentException("No repayment schedule exists for this loan.");

        var paidAt = DateTime.UtcNow;

        if (kind == RepaymentKind.LumpSum)
        {
            await ApplyLumpSumAsync(loan, schedule, request.Amount, paidAt, request.Note, ct);
        }
        else
        {
            await ApplyScheduledAsync(loan, schedule, request.Amount, paidAt, request.Note, ct);
        }

        await _repaymentRepository.SaveChangesAsync(ct);
        return await GetSummaryAsync(request.LoanId, ct);
    }

    /// <summary>
    /// Settles the loan in full for the current server-calculated quote
    /// (the outstanding balance). Records surplus above the quote.
    /// </summary>
    public async Task<RepaymentSummaryDto> SettleAsync(
        Guid memberId,
        MakeRepaymentRequest request,
        CancellationToken ct = default)
    {
        if (request.Amount <= 0)
        {
            throw new InvalidRepaymentException("Settlement amount must be greater than zero.");
        }

        var loan = await _repaymentRepository.GetLoanAsync(request.LoanId, ct)
            ?? throw new InvalidRepaymentException("Loan not found.");

        if (loan.MemberId != memberId)
        {
            throw new InvalidRepaymentException("A member can only settle their own loans.");
        }

        if (loan.Status != LoanStatus.Disbursed)
        {
            throw new InvalidRepaymentException("Only an active disbursed loan can be settled.");
        }

        var schedule = await _repaymentRepository.GetCurrentScheduleAsync(request.LoanId, ct);
        var quote = loan.OutstandingBalance;

        if (request.Amount < quote)
        {
            throw new InvalidRepaymentException(
                $"Settlement amount is less than the outstanding balance. Outstanding: ₦{quote:N2}.");
        }

        var repayment = new Repayment(
            loan.Id,
            schedule?.Version ?? 0,
            RepaymentKind.FullSettlement,
            quote,
            request.Amount,
            DateTime.UtcNow,
            request.Note);

        schedule?.Close();

        loan.ReduceOutstandingBalance(quote);

        await _repaymentRepository.AddAsync(repayment, ct);
        await _auditLog.RecordAsync(memberId, "Repayment.Settled", "Loan", loan.Id,
            $"Quote={quote:N2}, Actual={request.Amount:N2}, Surplus={(request.Amount - quote):N2}", ct);

        loan.MarkCompleted();
        await _repaymentRepository.SaveChangesAsync(ct);

        return await GetSummaryAsync(request.LoanId, ct);
    }

    private async Task ApplyScheduledAsync(
        Loan loan,
        LoanSchedule schedule,
        decimal amount,
        DateTime paidAt,
        string? note,
        CancellationToken ct)
    {
        var next = schedule.Items.Where(i => !i.IsPaid).OrderBy(i => i.Sequence).FirstOrDefault();
        if (next is null)
        {
            throw new InvalidRepaymentException("This loan has no remaining scheduled instalments.");
        }

        var needed = next.ExpectedAmount - next.PaidAmount;
        var apply = Math.Min(amount, needed);
        next.MarkPaid(apply, paidAt);

        // Guard against over-reducing: never reduce outstanding below zero.
        var reduceBy = Math.Min(amount, loan.OutstandingBalance);
        loan.ReduceOutstandingBalance(reduceBy);

        var repayment = new Repayment(
            loan.Id,
            schedule.Version,
            RepaymentKind.Scheduled,
            needed,
            amount,
            paidAt,
            note);
        await _repaymentRepository.AddAsync(repayment, ct);

        await _auditLog.RecordAsync(loan.MemberId, "Repayment.Scheduled", "Loan", loan.Id,
            $"Expected={needed:N2}, Actual={amount:N2}, Surplus={(amount - needed):N2}", ct);

        if (loan.OutstandingBalance <= 0)
        {
            loan.MarkCompleted();
            schedule.Close();
        }
    }

    private async Task ApplyLumpSumAsync(
        Loan loan,
        LoanSchedule schedule,
        decimal amount,
        DateTime paidAt,
        string? note,
        CancellationToken ct)
    {
        var reduceBy = Math.Min(amount, loan.OutstandingBalance);
        loan.ReduceOutstandingBalance(reduceBy);

        var repayment = new Repayment(
            loan.Id,
            schedule.Version,
            RepaymentKind.LumpSum,
            0m,
            amount,
            paidAt,
            note);
        await _repaymentRepository.AddAsync(repayment, ct);

        await _auditLog.RecordAsync(loan.MemberId, "Repayment.LumpSum", "Loan", loan.Id,
            $"PrincipalReduced={reduceBy:N2}, Actual={amount:N2}", ct);

        if (loan.OutstandingBalance <= 0)
        {
            loan.MarkCompleted();
            schedule.Close();
            return;
        }

        // Recalculate the remaining schedule as a new revision, preserving the
        // repayment frequency (ADR: default preferred behaviour).
        var revision = LoanSchedule.CreateRevision(
            schedule,
            loan.OutstandingBalance,
            loan.ApprovedFrequency!.Value,
            paidAt);

        await _repaymentRepository.AddAsync(revision, ct);
        await _auditLog.RecordAsync(loan.MemberId, "Repayment.ScheduleRevised", "LoanSchedule", revision.Id,
            $"Version={revision.Version}, RemainingBalance={loan.OutstandingBalance:N2}", ct);
    }

    private static ScheduleItemDto ToItemDto(LoanScheduleItem item) => new()
    {
        Id = item.Id,
        Sequence = item.Sequence,
        DueDateUtc = item.DueDateUtc,
        PrincipalDue = item.PrincipalDue,
        InterestDue = item.InterestDue,
        ExpectedAmount = item.ExpectedAmount,
        PaidAmount = item.PaidAmount,
        PaidAtUtc = item.PaidAtUtc,
        Status = item.Status,
    };

    private static ScheduleDto ToScheduleDto(LoanSchedule schedule, bool isCurrent) => new()
    {
        Id = schedule.Id,
        LoanId = schedule.LoanId,
        Version = schedule.Version,
        CreatedAtUtc = schedule.CreatedAtUtc,
        IsCurrent = isCurrent,
        Items = schedule.Items.OrderBy(i => i.Sequence).Select(ToItemDto).ToList(),
    };

    private static RepaymentDto ToRepaymentDto(Repayment r, string fundName = "") => new()
    {
        Id = r.Id,
        LoanId = r.LoanId,
        FundName = fundName,
        ScheduleVersion = r.ScheduleVersion,
        Kind = r.Kind,
        ExpectedAmount = r.ExpectedAmount,
        ActualAmount = r.ActualAmount,
        Surplus = r.Surplus,
        PaidAtUtc = r.PaidAtUtc,
        Note = r.Note,
    };
}
