using FamilyTrustFund.Domain.Evidence;

namespace FamilyTrustFund.Application.Evidence;

/// <summary>Persistence port for payment-evidence metadata.</summary>
public interface IEvidenceRepository
{
    Task<PaymentEvidence?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentEvidence>> GetForResourceAsync(
        string resourceType,
        Guid resourceId,
        CancellationToken ct = default);

    Task<IReadOnlyList<PaymentEvidence>> GetByUploaderAsync(
        Guid uploaderId,
        CancellationToken ct = default);

    void Add(PaymentEvidence evidence);

    Task SaveChangesAsync(CancellationToken ct = default);
}
