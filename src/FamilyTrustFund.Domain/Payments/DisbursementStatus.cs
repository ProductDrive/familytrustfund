namespace FamilyTrustFund.Domain.Payments;

/// <summary>
/// Disbursement lifecycle status of a single transfer to a recipient.
/// </summary>
public enum DisbursementStatus
{
    /// <summary>Transfer initiated with the provider but not yet confirmed.</summary>
    Pending = 1,

    /// <summary>Provider webhook confirmed a successful transfer.</summary>
    Successful = 2,

    /// <summary>Provider webhook reported a failed transfer.</summary>
    Failed = 3,

    /// <summary>Provider webhook reported the transfer was reversed.</summary>
    Reversed = 4,
}
