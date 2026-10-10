using FamilyTrustFund.Application.Auth;
using FamilyTrustFund.Domain.Auth;
using FamilyTrustFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyTrustFund.Infrastructure.Auth;

public class TermsAcceptanceRepository : ITermsAcceptanceRepository
{
    private readonly ApplicationDbContext _db;

    public TermsAcceptanceRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsAsync(Guid userId, string version, CancellationToken ct = default) =>
        _db.TermsAcceptances.AnyAsync(t => t.UserId == userId && t.Version == version, ct);

    public void Add(TermsAcceptance acceptance) => _db.TermsAcceptances.Add(acceptance);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}