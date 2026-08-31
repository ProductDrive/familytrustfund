using FamilyTrustFund.Domain.Funds;

namespace FamilyTrustFund.Application.Funds;

/// <summary>
/// Persistence port for funds. Implemented by the infrastructure layer.
/// </summary>
public interface IFundRepository
{
    Task<Fund?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Fund>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<List<Fund>> GetByGuarantorAsync(Guid guarantorId, CancellationToken ct = default);
    Task<Fund?> GetByJoinCodeAsync(string joinCode, CancellationToken ct = default);
    Task<bool> JoinCodeExistsAsync(Guid guarantorId, string joinCode, CancellationToken ct = default);
    void Add(Fund fund);
    Task SaveChangesAsync(CancellationToken ct = default);
}
