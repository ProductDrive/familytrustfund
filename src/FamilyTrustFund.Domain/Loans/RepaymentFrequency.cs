namespace FamilyTrustFund.Domain.Loans;

/// <summary>
/// How often loan repayments are due. The frequency is selected by the member
/// at request time and may be changed by the Guarantor during approval.
/// Once approved, the frequency is part of the immutable loan terms (ADR-010).
/// </summary>
public enum RepaymentFrequency
{
    /// <summary>Repayment due every week.</summary>
    Weekly = 1,

    /// <summary>Repayment due every two weeks.</summary>
    Biweekly = 2,

    /// <summary>Repayment due every month.</summary>
    Monthly = 3,
}
