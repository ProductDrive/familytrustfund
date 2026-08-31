using FamilyTrustFund.Domain.Contributions;

namespace FamilyTrustFund.Application.Contributions;

/// <summary>
/// Persistence port for Family fund contributions. Implemented by the
/// infrastructure layer.
/// </summary>
public interface IContributionRepository
{
    Task<FundContribution?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Returns pending contributions across funds owned by the specified
    /// Guarantor. Used for the Guarantor confirmation queue.
    /// </summary>
    Task<IReadOnlyList<ContributionWithDetails>> GetPendingForGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all contributions for a specific fund, including member details.
    /// Ownership is enforced by the caller.
    /// </summary>
    Task<IReadOnlyList<ContributionWithDetails>> GetByFundAsync(
        Guid fundId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all contributions for a specific member across all their funds.
    /// </summary>
    Task<IReadOnlyList<ContributionWithDetails>> GetByMemberAsync(
        Guid memberId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the member's confirmed Fund Credit within a specific fund.
    /// </summary>
    Task<decimal> GetConfirmedFundCreditAsync(
        Guid memberId,
        Guid fundId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the total confirmed Family Contributions across all members of
    /// a fund. Used for Family Capital transition eligibility (ADR-008).
    /// </summary>
    Task<decimal> SumConfirmedByFundAsync(
        Guid fundId,
        CancellationToken ct = default);

    /// <summary>
    /// Checks whether the member has an active disbursed loan in the fund,
    /// which prevents them from contributing (ADR-006).
    /// </summary>
    Task<bool> HasActiveDisbursedLoanAsync(
        Guid memberId,
        Guid fundId,
        CancellationToken ct = default);

    void Add(FundContribution contribution);

    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// Contribution projection that includes member and fund details for display.
/// </summary>
public sealed class ContributionWithDetails
{
    public FundContribution Contribution { get; init; } = null!;
    public Guid MemberId { get; init; }
    public string MemberDisplayName { get; init; } = string.Empty;
    public string MemberEmail { get; init; } = string.Empty;
    public Guid FundId { get; init; }
    public string FundName { get; init; } = string.Empty;
    public Guid GuarantorId { get; init; }
}
