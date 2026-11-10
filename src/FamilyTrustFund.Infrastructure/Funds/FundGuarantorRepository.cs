using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Domain.Auth;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Infrastructure.Data;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace FamilyTrustFund.Infrastructure.Funds;

public class FundGuarantorRepository : IFundGuarantorRepository
{
    private readonly ApplicationDbContext _db;

    public FundGuarantorRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<FundGuarantor?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.FundGuarantors.FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<FundGuarantor?> GetAsync(Guid fundId, Guid guarantorId, CancellationToken ct = default) =>
        _db.FundGuarantors.FirstOrDefaultAsync(
            g => g.FundId == fundId && g.GuarantorId == guarantorId,
            ct);

    public async Task<IReadOnlyList<FundGuarantor>> GetByFundAsync(
        Guid fundId,
        CancellationToken ct = default) =>
        await _db.FundGuarantors
            .Where(g => g.FundId == fundId)
            .OrderBy(g => g.Role)
            .ThenBy(g => g.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FundGuarantor>> GetActiveByFundAsync(
        Guid fundId,
        CancellationToken ct = default) =>
        await _db.FundGuarantors
            .Where(g => g.FundId == fundId && g.Status == FundGuarantorStatus.Active)
            .OrderBy(g => g.Role)
            .ThenBy(g => g.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FundGuarantorWithDetails>> GetByGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default) =>
        await (
                from g in _db.FundGuarantors
                join f in _db.Funds on g.FundId equals f.Id
                join originator in _db.Users on f.GuarantorId equals originator.Id
                where g.GuarantorId == guarantorId
                    && g.Status != FundGuarantorStatus.Revoked
                orderby g.CreatedAtUtc descending
                select new FundGuarantorWithDetails
                {
                    Membership = g,
                    FundId = f.Id,
                    FundName = f.Name,
                    FundType = f.Type,
                    OriginatorId = f.GuarantorId,
                    OriginatorDisplayName = originator.DisplayName,
                    OriginatorEmail = originator.Email ?? string.Empty,
                })
            .ToListAsync(ct);

    public Task<int> CountActiveAsync(Guid fundId, CancellationToken ct = default) =>
        _db.FundGuarantors.CountAsync(
            g => g.FundId == fundId && g.Status == FundGuarantorStatus.Active,
            ct);

    public async Task<GuarantorAccount?> FindGuarantorByEmailAsync(
        string email,
        CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = await _db.Users
            .Where(u => u.Email != null && u.Email.ToLower() == normalized)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                u.GuarantorApprovalStatus,
            })
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            return null;
        }

        return new GuarantorAccount
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            IsApprovedGuarantor = user.GuarantorApprovalStatus == GuarantorApprovalStatus.Approved,
        };
    }

    public async Task<IReadOnlyDictionary<Guid, GuarantorProfile>> GetProfilesAsync(
        IEnumerable<Guid> guarantorIds,
        CancellationToken ct = default)
    {
        var ids = guarantorIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, GuarantorProfile>();
        }

        return await _db.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new GuarantorProfile
            {
                Id = u.Id,
                DisplayName = u.DisplayName,
                Email = u.Email ?? string.Empty,
            })
            .ToDictionaryAsync(p => p.Id, ct);
    }

    public void Add(FundGuarantor membership) => _db.FundGuarantors.Add(membership);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
