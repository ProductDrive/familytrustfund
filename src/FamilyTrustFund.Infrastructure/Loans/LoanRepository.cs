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

    public Task<Loan?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken ct = default) =>
        _db.Loans.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task<IReadOnlyList<LoanWithDetails>> GetPendingForGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default) =>
        await _db.Loans
            .Where(l => l.Status == LoanStatus.Pending)
            .Join(_db.Funds, l => l.FundId, f => f.Id, (l, f) => new { Loan = l, Fund = f })
            .Where(x =>
                // Group Guarantors only see loans they still have to decide on.
                _db.LoanApprovals.Any(a =>
                    a.LoanId == x.Loan.Id
                    && a.GuarantorId == guarantorId
                    && a.Status == LoanApprovalStatus.Pending)
                // Legacy single-Guarantor loans may predate approval rows.
                || (x.Fund.GuarantorId == guarantorId
                    && !_db.LoanApprovals.Any(a => a.LoanId == x.Loan.Id)))
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
                GuarantorMode = x.Fund.GuarantorMode,
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
                GuarantorMode = f.GuarantorMode,
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
                GuarantorMode = x.Fund.GuarantorMode,
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

    public Task<bool> HasPendingInFundAsync(Guid fundId, CancellationToken ct = default) =>
        _db.Loans.AnyAsync(
            l => l.FundId == fundId && l.Status == LoanStatus.Pending,
            ct);

    public async Task<IReadOnlyList<LoanApproval>> GetApprovalsAsync(
        Guid loanId,
        CancellationToken ct = default) =>
        await _db.LoanApprovals
            .Where(a => a.LoanId == loanId)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(ct);

    public Task<LoanApproval?> GetApprovalAsync(
        Guid loanId,
        Guid guarantorId,
        CancellationToken ct = default) =>
        _db.LoanApprovals.FirstOrDefaultAsync(
            a => a.LoanId == loanId && a.GuarantorId == guarantorId,
            ct);

    public async Task<IReadOnlyList<LoanApprovalWithDetails>> GetApprovalsWithDetailsAsync(
        IReadOnlyCollection<Guid> loanIds,
        CancellationToken ct = default)
    {
        if (loanIds.Count == 0)
        {
            return Array.Empty<LoanApprovalWithDetails>();
        }

        return await _db.LoanApprovals
            .Where(a => loanIds.Contains(a.LoanId))
            .Join(_db.Users, a => a.GuarantorId, u => u.Id, (a, u) => new LoanApprovalWithDetails
            {
                Approval = a,
                GuarantorDisplayName = u.DisplayName,
                GuarantorEmail = u.Email ?? string.Empty,
            })
            .OrderBy(x => x.Approval.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public void AddApproval(LoanApproval approval) => _db.LoanApprovals.Add(approval);

    public void AddApprovals(IEnumerable<LoanApproval> approvals) => _db.LoanApprovals.AddRange(approvals);

    public void Add(Loan loan) => _db.Loans.Add(loan);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Two Guarantors raced to finalise the same loan. The caller reloads
            // and returns the committed state instead of double-processing.
            throw new LoanDecisionConflictException(
                "The loan was updated by another Guarantor. Reload the loan and try again.",
                ex);
        }
    }
}
