using FamilyTrustFund.Application.Repayments;
using FamilyTrustFund.Domain.Repayments;
using FamilyTrustFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyTrustFund.Infrastructure.Repayments;

public class PendingRepaymentRepository : IPendingRepaymentRepository
{
    private readonly ApplicationDbContext _db;

    public PendingRepaymentRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<PendingRepayment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.PendingRepayments.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<bool> HasPendingForLoanAsync(Guid loanId, CancellationToken ct = default) =>
        _db.PendingRepayments.AnyAsync(
            p => p.LoanId == loanId && p.Status == PendingRepaymentStatus.PendingConfirmation,
            ct);

    public Task<bool> HasPendingByMemberAndFundAsync(
        Guid memberId,
        Guid fundId,
        CancellationToken ct = default) =>
        _db.PendingRepayments
            .Join(_db.Loans, p => p.LoanId, l => l.Id, (p, l) => new { Pending = p, Loan = l })
            .AnyAsync(
                x => x.Pending.MemberId == memberId
                    && x.Loan.FundId == fundId
                    && x.Pending.Status == PendingRepaymentStatus.PendingConfirmation,
                ct);

    public async Task<IReadOnlyList<PendingRepaymentWithDetails>> GetPendingForGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default) =>
        await _db.PendingRepayments
            .Where(p => p.Status == PendingRepaymentStatus.PendingConfirmation)
            .Join(_db.Loans, p => p.LoanId, l => l.Id, (p, l) => new { Pending = p, Loan = l })
            .Join(_db.Funds, x => x.Loan.FundId, f => f.Id, (x, f) => new { x.Pending, x.Loan, Fund = f })
            .Where(x => x.Fund.GuarantorId == guarantorId)
            .Join(_db.Users, x => x.Pending.MemberId, u => u.Id, (x, u) => new PendingRepaymentWithDetails
            {
                Pending = x.Pending,
                MemberId = x.Pending.MemberId,
                MemberDisplayName = u.DisplayName,
                MemberEmail = u.Email ?? string.Empty,
                FundId = x.Fund.Id,
                FundName = x.Fund.Name,
                GuarantorId = x.Fund.GuarantorId,
            })
            .OrderByDescending(x => x.Pending.ReportedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PendingRepaymentWithDetails>> GetByMemberAsync(
        Guid memberId,
        CancellationToken ct = default) =>
        await _db.PendingRepayments
            .Where(p => p.MemberId == memberId)
            .Join(_db.Loans, p => p.LoanId, l => l.Id, (p, l) => new { Pending = p, Loan = l })
            .Join(_db.Funds, x => x.Loan.FundId, f => f.Id, (x, f) => new { x.Pending, x.Loan, Fund = f })
            .Join(_db.Users, x => x.Pending.MemberId, u => u.Id, (x, u) => new PendingRepaymentWithDetails
            {
                Pending = x.Pending,
                MemberId = x.Pending.MemberId,
                MemberDisplayName = u.DisplayName,
                MemberEmail = u.Email ?? string.Empty,
                FundId = x.Fund.Id,
                FundName = x.Fund.Name,
                GuarantorId = x.Fund.GuarantorId,
            })
            .OrderByDescending(x => x.Pending.ReportedAtUtc)
            .ToListAsync(ct);

    public void Add(PendingRepayment pending) => _db.PendingRepayments.Add(pending);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
