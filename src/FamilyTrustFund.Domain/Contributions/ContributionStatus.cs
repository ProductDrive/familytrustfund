namespace FamilyTrustFund.Domain.Contributions;

/// <summary>
/// Lifecycle of a Family fund contribution.
/// </summary>
/// <remarks>
/// A manually-reported contribution is placed in <see cref="PendingConfirmation"/>
/// and only becomes financial reality when the Guarantor confirms it. Uploaded
/// evidence alone never changes the ledger (ADR-016).
/// </remarks>
public enum ContributionStatus
{
    /// <summary>Member has reported the contribution; awaiting Guarantor confirmation.</summary>
    PendingConfirmation = 1,

    /// <summary>Guarantor confirmed the contribution; it is posted to Fund Credit.</summary>
    Confirmed = 2,

    /// <summary>Guarantor rejected the contribution; it does not affect Fund Credit.</summary>
    Rejected = 3,
}
