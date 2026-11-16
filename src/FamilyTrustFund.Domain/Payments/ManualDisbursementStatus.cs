namespace FamilyTrustFund.Domain.Payments;

/// <summary>
/// Lifecycle of an offline/manual loan disbursement reported by the Guarantor
/// and confirmed by the member (ADR-056).
/// </summary>
public enum ManualDisbursementStatus
{
    /// <summary>Guarantor is preparing the report; not yet visible to the member.</summary>
    Draft = 1,

    /// <summary>Reported and submitted; awaiting the member's confirmation or dispute.</summary>
    PendingMemberConfirmation = 2,

    /// <summary>The member confirmed receipt; the loan is disbursed.</summary>
    Confirmed = 3,

    /// <summary>The member disputed receipt; awaiting Guarantor resolution.</summary>
    Disputed = 4,

    /// <summary>The Guarantor withdrew the report; it never disbursed the loan.</summary>
    Cancelled = 5,
}
