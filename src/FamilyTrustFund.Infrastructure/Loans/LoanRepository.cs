using FamilyTrustFund.Application.Loans;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyTrustFund.Infrastructure.Loans;

public class LoanRepository : ILoanRepository
{
    private readonly ApplicationDbContext _db;

    public LoanRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<Loan?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Loans.FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task<IReadOnlyList<LoanWithDetails>> GetPendingForGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default) =>
        await _db.Loans
            .Where(l => l.Status == LoanStatus.Pending)
            .Join(_db.Funds, l => l.FundId, f => f.Id, (l, f) => new { Loan = l, Fund = f })
            .Where(x => x.Fund.GuarantorId == guarantorId)
            .Join(_db.Users, x => x.Loan.MemberId, u => u.Id, (x, u) => new LoanWithDetails
            {
                Loan = x.Loan,
                MemberId = x.Loan.MemberId,
                MemberDisplayName = u.DisplayName,
                MemberEmail = u.Email ?? string.Empty,
                FundId = x.Fund.Id,
                FundName = x.Fund.Name,
                FundType = x.Fund.Type,
                GuarantorId = x.Fund.GuarantorId,
            })
            .OrderByDescending(x => x.Loan.RequestedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LoanWithDetails>> GetByFundAsync(
        Guid fundId,
        CancellationToken ct = default) =>
        await _db.Loans
            .Where(l => l.FundId == fundId)
            .Join(_db.Users, l => l.MemberId, u => u.Id, (l, u) => new { Loan = l, User = u })
            .Join(_db.Funds, x => x.Loan.FundId, f => f.Id, (x, f) => new LoanWithDetails
            {
                Loan = x.Loan,
                MemberId = x.Loan.MemberId,
                MemberDisplayName = x.User.DisplayName,
                MemberEmail = x.User.Email ?? string.Empty,
                FundId = f.Id,
                FundName = f.Name,
                FundType = f.Type,
                GuarantorId = f.GuarantorId,
            })
            .OrderByDescending(x => x.Loan.RequestedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LoanWithDetails>> GetByMemberAsync(
        Guid memberId,
        CancellationToken ct = default) =>
        await _db.Loans
            .Where(l => l.MemberId == memberId)
            .Join(_db.Funds, l => l.FundId, f => f.Id, (l, f) => new { Loan = l, Fund = f })
            .Join(_db.Users, x => x.Loan.MemberId, u => u.Id, (x, u) => new LoanWithDetails
            {
                Loan = x.Loan,
                MemberId = x.Loan.MemberId,
                MemberDisplayName = u.DisplayName,
                MemberEmail = u.Email ?? string.Empty,
                FundId = x.Fund.Id,
                FundName = x.Fund.Name,
                FundType = x.Fund.Type,
                GuarantorId = x.Fund.GuarantorId,
            })
            .OrderByDescending(x => x.Loan.RequestedAtUtc)
            .ToListAsync(ct);

    public Task<int> CountActiveByMemberAsync(
        Guid memberId,
        CancellationToken ct = default) =>
        _db.Loans.CountAsync(
            l => l.MemberId == memberId
                && (l.Status == LoanStatus.Pending
                    || l.Status == LoanStatus.Approved
                    || l.Status == LoanStatus.Disbursed),
            ct);

    public Task<decimal> SumActiveDisbursedByFundAsync(
        Guid fundId,
        CancellationToken ct = default) =>
        _db.Loans
            .Where(l => l.FundId == fundId && l.Status == LoanStatus.Disbursed)
            .SumAsync(l => l.ApprovedAmount ?? l.RequestedAmount, ct);

    public Task<bool> HasPendingRequestAsync(
        Guid memberId,
        Guid fundId,
        CancellationToken ct = default) =>
        _db.Loans.AnyAsync(
            l => l.MemberId == memberId
                && l.FundId == fundId
                && l.Status == LoanStatus.Pending,
            ct);

    public void Add(Loan loan) => _db.Loans.Add(loan);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
