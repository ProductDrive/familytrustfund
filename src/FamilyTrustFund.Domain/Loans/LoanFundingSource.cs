namespace FamilyTrustFund.Domain.Loans;

/// <summary>
/// Identifies which capital pool funds a loan.
/// </summary>
/// <remarks>
/// Before Family Capital transition, loans are funded by Guarantor committed
/// capital. After transition, new loans are funded from Family Capital.
/// </remarks>
public enum LoanFundingSource
{
    /// <summary>Loan funded from Guarantor committed capital.</summary>
    GuarantorCapital = 1,

    /// <summary>Loan funded from Family Capital (post-transition).</summary>
    FamilyCapital = 2,
}
