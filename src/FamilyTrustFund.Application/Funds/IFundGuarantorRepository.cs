using FamilyTrustFund.Domain.Funds;

namespace FamilyTrustFund.Application.Funds;

/// <summary>
/// Persistence port for Group Guarantor memberships and invitations (ADR-051).
/// Implemented by the infrastructure layer.
/// </summary>
public interface IFundGuarantorRepository
{
    Task<FundGuarantor?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<FundGuarantor?> GetAsync(Guid fundId, Guid guarantorId, CancellationToken ct = default);

    Task<IReadOnlyList<FundGuarantor>> GetByFundAsync(Guid fundId, CancellationToken ct = default);

    Task<IReadOnlyList<FundGuarantor>> GetActiveByFundAsync(Guid fundId, CancellationToken ct = default);

    /// <summary>
    /// All group memberships and invitations for one Guarantor, with fund and
    /// originator details for display.
    /// </summary>
    Task<IReadOnlyList<FundGuarantorWithDetails>> GetByGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default);

    /// <summary>Count of active Guarantors in a fund, including the originator.</summary>
    Task<int> CountActiveAsync(Guid fundId, CancellationToken ct = default);

    /// <summary>
    /// Looks up an existing Guarantor account by email. Invitations may only be
    /// sent to an approved Guarantor account (ADR-051).
    /// </summary>
    Task<GuarantorAccount?> FindGuarantorByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>Display names and emails for a set of Guarantor user ids.</summary>
    Task<IReadOnlyDictionary<Guid, GuarantorProfile>> GetProfilesAsync(
        IEnumerable<Guid> guarantorIds,
        CancellationToken ct = default);

    void Add(FundGuarantor membership);

    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>A group-fund membership row plus the context needed to display it.</summary>
public sealed class FundGuarantorWithDetails
{
    public FundGuarantor Membership { get; init; } = null!;
    public Guid FundId { get; init; }
    public string FundName { get; init; } = string.Empty;
    public FundType FundType { get; init; }
    public Guid OriginatorId { get; init; }
    public string OriginatorDisplayName { get; init; } = string.Empty;
    public string OriginatorEmail { get; init; } = string.Empty;
}

/// <summary>A Guarantor account candidate for a fund invitation.</summary>
public sealed class GuarantorAccount
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsApprovedGuarantor { get; init; }
}

/// <summary>Display details for a Guarantor.</summary>
public sealed class GuarantorProfile
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}
