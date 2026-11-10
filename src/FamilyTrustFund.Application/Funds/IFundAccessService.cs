using FamilyTrustFund.Domain.Funds;

namespace FamilyTrustFund.Application.Funds;

/// <summary>
/// Resolves which Guarantors may operate a fund. A single-Guarantor fund is
/// operated by its owner; a Group Guarantor fund is operated by its Fund
/// Originator plus every active co-guarantor (ADR-051).
/// </summary>
/// <remarks>
/// Every Guarantor-facing operation must route its ownership check through this
/// port. Never compare <see cref="Fund.GuarantorId"/> directly outside it: that
/// field identifies the Fund Originator, not the whole operating group.
/// </remarks>
public interface IFundAccessService
{
    /// <summary>True when the user is the Fund Originator or an active co-guarantor.</summary>
    Task<bool> CanAccessFundAsync(Fund fund, Guid userId, CancellationToken ct = default);

    /// <summary>True when the user is the Fund Originator or an active co-guarantor.</summary>
    Task<bool> CanAccessFundAsync(Guid fundId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// True only for the Fund Originator — the Guarantor who created the fund
    /// and the only one who may initiate disbursement (ADR-053).
    /// </summary>
    Task<bool> IsOriginatorAsync(Guid fundId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// The Guarantors required to decide a loan request: every active Guarantor
    /// of a group fund, or the sole owner of a single-Guarantor fund.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetRequiredApproverIdsAsync(Fund fund, CancellationToken ct = default);
}
