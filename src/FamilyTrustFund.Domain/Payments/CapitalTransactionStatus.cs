namespace FamilyTrustFund.Domain.Payments;

/// <summary>
/// Lifecycle status of a Guarantor capital payment into the platform account.
/// </summary>
public enum CapitalTransactionStatus
{
    /// <summary>Collection initiated with the provider but not yet confirmed.</summary>
    PendingConfirmation = 1,

    /// <summary>Provider-confirmed collection; money is available to disburse.</summary>
    Confirmed = 2,

    /// <summary>Provider reported the collection failed.</summary>
    Failed = 3,
}