namespace FamilyTrustFund.Domain.Payments;

/// <summary>
/// Verification/active state of a payment recipient.
/// </summary>
public enum PaymentRecipientStatus
{
    /// <summary>Recipient details supplied but not yet verified with the provider.</summary>
    Unverified = 1,

    /// <summary>Recipient verified and available for disbursements.</summary>
    Active = 2,

    /// <summary>Recipient disabled; no further disbursements should use it.</summary>
    Disabled = 3,
}
