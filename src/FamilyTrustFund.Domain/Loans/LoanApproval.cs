namespace FamilyTrustFund.Domain.Loans;

/// <summary>
/// One Guarantor's individual decision on a pending loan request (ADR-052).
/// </summary>
/// <remarks>
/// The set of rows for a loan is the approval snapshot: it is created from the
/// fund's active Guarantors at request time, so membership changes can never
/// silently alter an existing loan's requirement. The unique index on
/// <c>(LoanId, GuarantorId)</c> makes a duplicate approval impossible.
///
/// Single-Guarantor funds get exactly one row, so the same code path applies to
/// both fund modes without special-casing.
/// </remarks>
public class LoanApproval
{
    public Guid Id { get; private set; }
    public Guid LoanId { get; private set; }
    public Guid GuarantorId { get; private set; }

    public LoanApprovalStatus Status { get; private set; }

    /// <summary>When this Guarantor recorded their decision.</summary>
    public DateTime? DecidedAtUtc { get; private set; }

    /// <summary>Optional decision comment supplied by the Guarantor.</summary>
    public string? Comment { get; private set; }

    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    protected LoanApproval() { }

    /// <summary>
    /// Creates a required (pending) approval for one Guarantor. Called once per
    /// active Guarantor when a loan request is created.
    /// </summary>
    public static LoanApproval Require(Guid loanId, Guid guarantorId)
    {
        if (loanId == Guid.Empty)
        {
            throw new InvalidLoanException("A loan is required for an approval.");
        }

        if (guarantorId == Guid.Empty)
        {
            throw new InvalidLoanException("A Guarantor is required for an approval.");
        }

        return new LoanApproval
        {
            Id = Guid.NewGuid(),
            LoanId = loanId,
            GuarantorId = guarantorId,
            Status = LoanApprovalStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    public bool IsPending => Status == LoanApprovalStatus.Pending;

    public bool IsApproved => Status == LoanApprovalStatus.Approved;

    /// <summary>Records this Guarantor's approval. Only a pending row can change.</summary>
    public void Approve(string? comment = null)
    {
        if (Status != LoanApprovalStatus.Pending)
        {
            throw new InvalidLoanException("You have already recorded a decision on this loan.");
        }

        EnsureValidComment(comment);
        Status = LoanApprovalStatus.Approved;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        DecidedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Records this Guarantor's rejection. Only a pending row can change.</summary>
    public void Reject(string? comment = null)
    {
        if (Status != LoanApprovalStatus.Pending)
        {
            throw new InvalidLoanException("You have already recorded a decision on this loan.");
        }

        EnsureValidComment(comment);
        Status = LoanApprovalStatus.Rejected;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        DecidedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Closes an outstanding requirement because the loan was resolved by
    /// another Guarantor's rejection or a cancellation.
    /// </summary>
    public void Cancel()
    {
        if (Status != LoanApprovalStatus.Pending)
        {
            return;
        }

        Status = LoanApprovalStatus.Cancelled;
        DecidedAtUtc = DateTime.UtcNow;
    }

    private static void EnsureValidComment(string? comment)
    {
        if (comment is not null && comment.Length > 1000)
        {
            throw new InvalidLoanException("Decision comment cannot exceed 1000 characters.");
        }
    }
}

/// <summary>State of one Guarantor's loan approval.</summary>
public enum LoanApprovalStatus
{
    /// <summary>Awaiting this Guarantor's decision.</summary>
    Pending = 1,

    /// <summary>This Guarantor approved the loan.</summary>
    Approved = 2,

    /// <summary>This Guarantor rejected the loan.</summary>
    Rejected = 3,

    /// <summary>The loan was resolved before this Guarantor decided.</summary>
    Cancelled = 4,
}
