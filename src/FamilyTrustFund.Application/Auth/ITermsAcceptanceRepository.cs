using FamilyTrustFund.Domain.Auth;

namespace FamilyTrustFund.Application.Auth;

/// <summary>
/// Persistence port for Guarantor terms acceptances. Immutable, versioned
/// records; never updated in place.
/// </summary>
public interface ITermsAcceptanceRepository
{
    Task<bool> ExistsAsync(Guid userId, string version, CancellationToken ct = default);

    void Add(TermsAcceptance acceptance);

    Task SaveChangesAsync(CancellationToken ct = default);
}