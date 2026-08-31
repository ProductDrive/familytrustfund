namespace FamilyTrustFund.Domain.Loans;

/// <summary>
/// Loan lifecycle statuses.
/// </summary>
public enum LoanStatus
{
    /// <summary>Member has submitted a loan request; awaiting Guarantor review.</summary>
    Pending = 1,

    /// <summary>Guarantor has approved the loan; disbursement may follow.</summary>
    Approved = 2,

    /// <summary>Guarantor has rejected the loan request.</summary>
    Rejected = 3,

    /// <summary>Loan disbursement has been initiated but not yet confirmed by provider.</summary>
    DisbursementPending = 4,

    /// <summary>Funds have been disbursed to the member.</summary>
    Disbursed = 5,

    /// <summary>Loan has been fully repaid or settled.</summary>
    Completed = 6,

    /// <summary>Loan has defaulted on repayments.</summary>
    Defaulted = 7,
}
