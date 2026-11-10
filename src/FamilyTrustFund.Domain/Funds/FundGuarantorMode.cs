namespace FamilyTrustFund.Domain.Funds;

/// <summary>
/// Whether a fund is operated by a single Guarantor or jointly by a group of
/// Guarantors (ADR-051).
/// </summary>
public enum FundGuarantorMode
{
    /// <summary>Existing behaviour: one Guarantor owns and operates the fund.</summary>
    Single = 0,

    /// <summary>
    /// A Fund Originator plus invited, independent Guarantor accounts that
    /// jointly operate the fund.
    /// </summary>
    Group = 1,
}
