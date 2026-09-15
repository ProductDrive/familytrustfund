using FamilyTrustFund.Application.Contributions;
using FamilyTrustFund.Domain.Contributions;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyTrustFund.Infrastructure.Contributions;

public class ContributionRepository : IContributionRepository
{
    private readonly ApplicationDbContext _db;

    public ContributionRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<FundContribution?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.FundContributions.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<ContributionWithDetails>> GetPendingForGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default) =>
        await _db.FundContributions
            .Where(c => c.Status == ContributionStatus.PendingConfirmation)
            .Join(_db.Funds, c => c.FundId, f => f.Id, (c, f) => new { Contribution = c, Fund = f })
            .Where(x => x.Fund.GuarantorId == guarantorId)
            .Join(_db.Users, x => x.Contribution.MemberId, u => u.Id, (x, u) => new ContributionWithDetails
            {
                Contribution = x.Contribution,
                MemberId = x.Contribution.MemberId,
                MemberDisplayName = u.DisplayName,
                MemberEmail = u.Email ?? string.Empty,
                FundId = x.Fund.Id,
                FundName = x.Fund.Name,
                GuarantorId = x.Fund.GuarantorId,
            })
            .OrderByDescending(x => x.Contribution.ReportedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ContributionWithDetails>> GetByFundAsync(
        Guid fundId,
        CancellationToken ct = default) =>
        await _db.FundContributions
            .Where(c => c.FundId == fundId)
            .Join(_db.Users, c => c.MemberId, u => u.Id, (c, u) => new { Contribution = c, User = u })
            .Join(_db.Funds, x => x.Contribution.FundId, f => f.Id, (x, f) => new ContributionWithDetails
            {
                Contribution = x.Contribution,
                MemberId = x.Contribution.MemberId,
                MemberDisplayName = x.User.DisplayName,
                MemberEmail = x.User.Email ?? string.Empty,
                FundId = f.Id,
                FundName = f.Name,
                GuarantorId = f.GuarantorId,
            })
            .OrderByDescending(x => x.Contribution.ReportedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ContributionWithDetails>> GetByMemberAsync(
        Guid memberId,
        CancellationToken ct = default) =>
        await _db.FundContributions
            .Where(c => c.MemberId == memberId)
            .Join(_db.Funds, c => c.FundId, f => f.Id, (c, f) => new { Contribution = c, Fund = f })
            .Join(_db.Users, x => x.Contribution.MemberId, u => u.Id, (x, u) => new ContributionWithDetails
            {
                Contribution = x.Contribution,
                MemberId = x.Contribution.MemberId,
                MemberDisplayName = u.DisplayName,
                MemberEmail = u.Email ?? string.Empty,
                FundId = x.Fund.Id,
                FundName = x.Fund.Name,
                GuarantorId = x.Fund.GuarantorId,
            })
            .OrderByDescending(x => x.Contribution.ReportedAtUtc)
            .ToListAsync(ct);

    public Task<decimal> GetConfirmedFundCreditAsync(
        Guid memberId,
        Guid fundId,
        CancellationToken ct = default) =>
        _db.FundContributions
            .Where(c => c.MemberId == memberId
                && c.FundId == fundId
                && c.Status == ContributionStatus.Confirmed)
            .SumAsync(c => c.Amount, ct);

    public Task<decimal> SumConfirmedByFundAsync(
        Guid fundId,
        CancellationToken ct = default) =>
        _db.FundContributions
            .Where(c => c.FundId == fundId && c.Status == ContributionStatus.Confirmed)
            .SumAsync(c => c.Amount, ct);

    public Task<bool> HasActiveDisbursedLoanAsync(
        Guid memberId,
        CancellationToken ct = default) =>
        _db.Loans.AnyAsync(
            l => l.MemberId == memberId
                && l.Status == LoanStatus.Disbursed,
            ct);

    public void Add(FundContribution contribution) => _db.FundContributions.Add(contribution);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
