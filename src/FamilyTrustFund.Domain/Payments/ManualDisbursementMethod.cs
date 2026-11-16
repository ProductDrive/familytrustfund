namespace FamilyTrustFund.Domain.Payments;

/// <summary>
/// How a Guarantor paid a member outside the payment provider (ADR-056).
/// </summary>
public enum ManualDisbursementMethod
{
    /// <summary>A direct bank transfer from the Guarantor to the member.</summary>
    BankTransfer = 1,

    /// <summary>Cash handed to the member.</summary>
    Cash = 2,

    /// <summary>Any other offline method.</summary>
    Other = 3,
}
