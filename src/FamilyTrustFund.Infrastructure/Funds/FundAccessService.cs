using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyTrustFund.Infrastructure.Funds;

public class FundAccessService : IFundAccessService
{
    private readonly ApplicationDbContext _db;

    public FundAccessService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<bool> CanAccessFundAsync(Fund fund, Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return false;
        }

        // The Fund Originator always has access, including single-Guarantor funds
        // which never create membership rows.
        if (fund.GuarantorId == userId)
        {
            return true;
        }

        if (!fund.IsGroupFund)
        {
            return false;
        }

        return await _db.FundGuarantors.AnyAsync(
            g => g.FundId == fund.Id
                && g.GuarantorId == userId
                && g.Status == FundGuarantorStatus.Active,
            ct);
    }

    public async Task<bool> CanAccessFundAsync(Guid fundId, Guid userId, CancellationToken ct = default)
    {
        var fund = await _db.Funds.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fundId, ct);
        return fund is not null && await CanAccessFundAsync(fund, userId, ct);
    }

    public async Task<bool> IsOriginatorAsync(Guid fundId, Guid userId, CancellationToken ct = default) =>
        await _db.Funds.AnyAsync(f => f.Id == fundId && f.GuarantorId == userId, ct);

    public async Task<IReadOnlyList<Guid>> GetRequiredApproverIdsAsync(
        Fund fund,
        CancellationToken ct = default)
    {
        if (!fund.IsGroupFund)
        {
            return new[] { fund.GuarantorId };
        }

        var active = await _db.FundGuarantors
            .Where(g => g.FundId == fund.Id && g.Status == FundGuarantorStatus.Active)
            .Select(g => g.GuarantorId)
            .ToListAsync(ct);

        // The originator always decides, even if their membership row is missing
        // (defensive: a group fund is created with that row).
        if (!active.Contains(fund.GuarantorId))
        {
            active.Insert(0, fund.GuarantorId);
        }

        return active;
    }
}
