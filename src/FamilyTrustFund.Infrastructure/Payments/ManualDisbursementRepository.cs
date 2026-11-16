using FamilyTrustFund.Application.Payments;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Payments;
using FamilyTrustFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyTrustFund.Infrastructure.Payments;

public class ManualDisbursementRepository : IManualDisbursementRepository
{
    private readonly ApplicationDbContext _db;

    public ManualDisbursementRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<ManualDisbursement?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.ManualDisbursements.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<ManualDisbursement?> GetLatestForLoanAsync(Guid loanId, CancellationToken ct = default) =>
        _db.ManualDisbursements
            .Where(r => r.LoanId == loanId)
            .OrderByDescending(r => r.ReportedAtUtc)
            .FirstOrDefaultAsync(ct);

    public Task<ManualDisbursement?> GetLiveForLoanAsync(Guid loanId, CancellationToken ct = default) =>
        _db.ManualDisbursements
            .Where(r => r.LoanId == loanId
                && (r.Status == ManualDisbursementStatus.Draft
                    || r.Status == ManualDisbursementStatus.PendingMemberConfirmation
                    || r.Status == ManualDisbursementStatus.Disputed))
            .OrderByDescending(r => r.ReportedAtUtc)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ManualDisbursementWithDetails>> GetForGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default) =>
        await _db.ManualDisbursements
            .Join(_db.Funds, r => r.FundId, f => f.Id, (r, f) => new { Report = r, Fund = f })
            .Where(x =>
                x.Fund.GuarantorId == guarantorId
                || _db.FundGuarantors.Any(g =>
                    g.FundId == x.Fund.Id
                    && g.GuarantorId == guarantorId
                    && g.Status == FundGuarantorStatus.Active))
            .Join(_db.Users, x => x.Report.MemberId, u => u.Id, (x, u) => new ManualDisbursementWithDetails
            {
                Report = x.Report,
                MemberId = x.Report.MemberId,
                MemberDisplayName = u.DisplayName,
                MemberEmail = u.Email ?? string.Empty,
                FundId = x.Fund.Id,
                FundName = x.Fund.Name,
                GuarantorId = x.Fund.GuarantorId,
            })
            .OrderByDescending(x => x.Report.ReportedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ManualDisbursementWithDetails>> GetForMemberAsync(
        Guid memberId,
        CancellationToken ct = default) =>
        await _db.ManualDisbursements
            .Where(r => r.MemberId == memberId)
            .Join(_db.Funds, r => r.FundId, f => f.Id, (r, f) => new { Report = r, Fund = f })
            .Join(_db.Users, x => x.Report.MemberId, u => u.Id, (x, u) => new ManualDisbursementWithDetails
            {
                Report = x.Report,
                MemberId = x.Report.MemberId,
                MemberDisplayName = u.DisplayName,
                MemberEmail = u.Email ?? string.Empty,
                FundId = x.Fund.Id,
                FundName = x.Fund.Name,
                GuarantorId = x.Fund.GuarantorId,
            })
            .OrderByDescending(x => x.Report.ReportedAtUtc)
            .ToListAsync(ct);

    public void Add(ManualDisbursement report) => _db.ManualDisbursements.Add(report);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
