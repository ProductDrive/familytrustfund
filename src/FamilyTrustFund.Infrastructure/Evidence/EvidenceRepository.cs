using FamilyTrustFund.Application.Evidence;
using FamilyTrustFund.Domain.Evidence;
using FamilyTrustFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyTrustFund.Infrastructure.Evidence;

public class EvidenceRepository : IEvidenceRepository
{
    private readonly ApplicationDbContext _db;

    public EvidenceRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<PaymentEvidence?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.PaymentEvidences.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<IReadOnlyList<PaymentEvidence>> GetForResourceAsync(
        string resourceType,
        Guid resourceId,
        CancellationToken ct = default) =>
        await _db.PaymentEvidences
            .Where(e => e.ResourceType == resourceType && e.ResourceId == resourceId)
            .OrderBy(e => e.UploadedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PaymentEvidence>> GetByUploaderAsync(
        Guid uploaderId,
        CancellationToken ct = default) =>
        await _db.PaymentEvidences
            .Where(e => e.UploadedByUserId == uploaderId)
            .OrderByDescending(e => e.UploadedAtUtc)
            .ToListAsync(ct);

    public void Add(PaymentEvidence evidence) => _db.PaymentEvidences.Add(evidence);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
