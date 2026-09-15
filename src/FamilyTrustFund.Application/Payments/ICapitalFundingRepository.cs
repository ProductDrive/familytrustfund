using FamilyTrustFund.Domain.Payments;

namespace FamilyTrustFund.Application.Payments;

/// <summary>
/// Persistence port for Guarantor capital payments (funded capital).
/// </summary>
public interface ICapitalFundingRepository
{
    Task<CapitalTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<CapitalTransaction?> GetByProviderReferenceAsync(string providerReference, CancellationToken ct = default);

    Task<IReadOnlyList<CapitalTransaction>> GetByFundAsync(
        Guid fundId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<int> CountByFundAsync(Guid fundId, CancellationToken ct = default);

    /// <summary>Total net amount of confirmed capital payments for a fund.</summary>
    Task<decimal> SumConfirmedNetByFundAsync(Guid fundId, CancellationToken ct = default);

    void Add(CapitalTransaction transaction);
    Task SaveChangesAsync(CancellationToken ct = default);
}