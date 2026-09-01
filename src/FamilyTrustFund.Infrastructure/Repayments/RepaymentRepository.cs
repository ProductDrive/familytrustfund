using FamilyTrustFund.Application.Repayments;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Repayments;
using FamilyTrustFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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

    public async Task AddAsync(LoanSchedule schedule, CancellationToken ct = default)
    {
        await _db.Set<LoanSchedule>().AddAsync(schedule, ct);
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
