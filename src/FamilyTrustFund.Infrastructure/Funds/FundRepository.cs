using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyTrustFund.Infrastructure.Funds;

public class FundRepository : IFundRepository
{
    private readonly ApplicationDbContext _db;

    public FundRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<Fund?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Funds.FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<List<Fund>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default) =>
        _db.Funds.Where(f => ids.Contains(f.Id)).ToListAsync(ct);

    public Task<List<Fund>> GetByGuarantorAsync(Guid guarantorId, CancellationToken ct = default) =>
        _db.Funds
            .Where(f => f.GuarantorId == guarantorId)
            .OrderByDescending(f => f.CreatedAtUtc)
            .ToListAsync(ct);

    public Task<Fund?> GetByJoinCodeAsync(string joinCode, CancellationToken ct = default) =>
        _db.Funds.FirstOrDefaultAsync(f => f.JoinCode == joinCode.Trim().ToUpperInvariant(), ct);

    public Task<bool> JoinCodeExistsAsync(Guid guarantorId, string joinCode, CancellationToken ct = default) =>
        _db.Funds.AnyAsync(
            f => f.GuarantorId == guarantorId && f.JoinCode == joinCode.Trim().ToUpperInvariant(),
            ct);

    public void Add(Fund fund) => _db.Funds.Add(fund);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
