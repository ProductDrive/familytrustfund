using FamilyTrustFund.Application.Repayments;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Repayments;
using FamilyTrustFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyTrustFund.Infrastructure.Repayments;

/// <summary>EF Core backed repayment repository.</summary>
public sealed class RepaymentRepository : IRepaymentRepository
{
    private readonly ApplicationDbContext _db;

    public RepaymentRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Loan?> GetLoanAsync(Guid loanId, CancellationToken ct = default)
    {
        return await _db.Set<Loan>().FirstOrDefaultAsync(x => x.Id == loanId, ct);
    }

    public async Task<LoanSchedule?> GetCurrentScheduleAsync(Guid loanId, CancellationToken ct = default)
    {
        return await _db.Set<LoanSchedule>()
            .Include(s => s.Items)
            .Where(s => s.LoanId == loanId)
            .OrderByDescending(s => s.Version)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<LoanSchedule>> GetAllSchedulesAsync(Guid loanId, CancellationToken ct = default)
    {
        var schedules = await _db.Set<LoanSchedule>()
            .Include(s => s.Items)
            .Where(s => s.LoanId == loanId)
            .ToListAsync(ct);
        return schedules;
    }

    public async Task<LoanScheduleItem?> GetItemAsync(Guid itemId, CancellationToken ct = default)
    {
        return await _db.Set<LoanScheduleItem>().FirstOrDefaultAsync(x => x.Id == itemId, ct);
    }

    public async Task<IReadOnlyList<Repayment>> GetRepaymentsAsync(Guid loanId, CancellationToken ct = default)
    {
        return await _db.Set<Repayment>().Where(x => x.LoanId == loanId).ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<RepaymentWithFund> Items, int TotalCount)> GetMyRepaymentsAsync(
        Guid memberId, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;

        var query = from repayment in _db.Repayments
                    join loan in _db.Loans on repayment.LoanId equals loan.Id
                    join fund in _db.Funds on loan.FundId equals fund.Id
                    where loan.MemberId == memberId
                    select new { repayment, fund.Name };

        var totalCount = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.repayment.PaidAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (rows.Select(x => new RepaymentWithFund { Repayment = x.repayment, FundName = x.Name }).ToList(), totalCount);
    }

    public async Task AddAsync(LoanSchedule schedule, CancellationToken ct = default)
    {
        await _db.Set<LoanSchedule>().AddAsync(schedule, ct);
    }

    /// <summary>
    /// Adds and saves a new schedule revision idempotently. The unique
    /// (LoanId, Version) index is the authoritative guard: if a concurrent
    /// request (e.g. the verify endpoint racing a provider webhook) has already
    /// persisted the same revision, the pending copy is detached and the
    /// existing schedule is returned so the caller can treat the operation as a
    /// no-op instead of surfacing a duplicate-key error.
    /// </summary>
    public async Task<LoanSchedule?> SaveNewScheduleAsync(LoanSchedule schedule, CancellationToken ct = default)
    {
        await _db.Set<LoanSchedule>().AddAsync(schedule, ct);

        try
        {
            await _db.SaveChangesAsync(ct);
            return schedule;
        }
        catch (DbUpdateException ex) when (IsUniqueScheduleConflict(ex))
        {
            return await DetachAndReturnExistingAsync(schedule, ct);
        }
        catch (PostgresException ex)
            when (ex.SqlState == PostgresErrorCodes.UniqueViolation
                  && ex.ConstraintName == "IX_loan_schedules_LoanId_Version")
        {
            return await DetachAndReturnExistingAsync(schedule, ct);
        }
    }

    /// <summary>
    /// EF Core surfaces a constraint violation as a <see cref="DbUpdateException"/>
    /// wrapping the underlying <see cref="PostgresException"/>, so the unique
    /// (LoanId, Version) conflict has to be found by unwrapping the chain.
    /// </summary>
    private static bool IsUniqueScheduleConflict(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pg
                && pg.SqlState == PostgresErrorCodes.UniqueViolation
                && pg.ConstraintName == "IX_loan_schedules_LoanId_Version")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Detaches the conflicting revision (and its items) without clearing the
    /// tracker, so other pending changes in this unit of work (e.g. the
    /// disbursement/loan status) are preserved, and returns the already-committed
    /// schedule so the caller can treat the operation as a no-op.
    /// </summary>
    private async Task<LoanSchedule?> DetachAndReturnExistingAsync(LoanSchedule schedule, CancellationToken ct)
    {
        _db.Entry(schedule).State = EntityState.Detached;
        foreach (var item in schedule.Items)
        {
            _db.Entry(item).State = EntityState.Detached;
        }

        return await GetCurrentScheduleAsync(schedule.LoanId, ct);
    }

    public async Task AddAsync(Repayment repayment, CancellationToken ct = default)
    {
        await _db.Set<Repayment>().AddAsync(repayment, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }
}
