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

    /// <summary>
    /// True when the join code is already used by any fund. Join codes identify a
    /// fund globally (ADR-051).
    /// </summary>
    Task<bool> JoinCodeExistsAsync(string joinCode, CancellationToken ct = default);
    void Add(Fund fund);
    Task SaveChangesAsync(CancellationToken ct = default);
}
