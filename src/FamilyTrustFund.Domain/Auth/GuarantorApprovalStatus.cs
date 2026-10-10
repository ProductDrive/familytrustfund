namespace FamilyTrustFund.Domain.Auth;

/// <summary>
/// State of a Guarantor account that self-registered through email OTP.
/// A pending Guarantor does not hold the <c>Guarantor</c> role yet; the
/// role-gated policies (AGENTS §4) keep every Guarantor capability blocked
/// until a Super Admin approves the account (slim single-endpoint approval).
/// </summary>
public enum GuarantorApprovalStatus
{
    /// <summary>Regular account; no Guarantor registration has been requested.</summary>
    NotRequested = 0,

    /// <summary>Self-registered as Guarantor; awaiting a Super Admin decision.</summary>
    Pending = 1,

    /// <summary>Super Admin approved the Guarantor; the Guarantor role was granted.</summary>
    Approved = 2,

    /// <summary>Super Admin rejected the Guarantor account.</summary>
    Rejected = 3,
}