namespace FamilyTrustFund.Domain.Funds;

/// <summary>
/// Lifecycle state of a fund.
/// </summary>
public enum FundStatus
{
    /// <summary>Accepting members and processing activity.</summary>
    Active = 1,

    /// <summary>Suspended by the Guarantor or administrative action.</summary>
    Inactive = 2,

    /// <summary>A Family fund that has transitioned to Family Capital. One-way.</summary>
    Transitioned = 3,
}
