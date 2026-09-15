namespace FamilyTrustFund.Domain.Repayments;

/// <summary>
/// A member-declared manual (external/offline) repayment awaiting Guarantor
/// confirmation. The repayment remains pending and does NOT affect the loan
/// ledger until the Guarantor confirms it (AGENTS §2.9, ADR-016/037).
/// Only confirmation posts the financial transaction.
/// </summary>
public class PendingRepayment
{
    public Guid Id { get; private set; }
    public Guid LoanId { get; private set; }
    public Guid MemberId { get; private set; }

    /// <summary>Amount the member declares having paid.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Optional member-supplied reference for the external payment.</summary>
    public string? Reference { get; private set; }

    /// <summary>Kind of repayment declared (scheduled instalment, lump sum, full settlement).</summary>
    public RepaymentKind Kind { get; private set; }

    public string? Note { get; private set; }

    public PendingRepaymentStatus Status { get; private set; }

    public string? RejectionReason { get; private set; }

    /// <summary>Optional free-text note added by the Guarantor when confirming.</summary>
    public string? ConfirmationNote { get; private set; }

    public DateTime ReportedAtUtc { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public DateTime? RejectedAtUtc { get; private set; }

    protected PendingRepayment()
    {
    }

    /// <summary>
    /// Creates a pending repayment declaration for a member's loan.
    /// </summary>
    public static PendingRepayment Report(
        Guid loanId,
        Guid memberId,
        decimal amount,
        RepaymentKind kind,
        string? reference = null,
        string? note = null)
    {
        if (loanId == Guid.Empty)
        {
            throw new InvalidRepaymentException("A loan is required for a repayment.");
        }

        if (memberId == Guid.Empty)
        {
            throw new InvalidRepaymentException("A member is required for a repayment.");
        }

        if (amount <= 0)
        {
            throw new InvalidRepaymentException("Repayment amount must be greater than zero.");
        }

        return new PendingRepayment
        {
            Id = Guid.NewGuid(),
            LoanId = loanId,
            MemberId = memberId,
            Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero),
            Kind = kind,
            Reference = reference,
            Note = note,
            Status = PendingRepaymentStatus.PendingConfirmation,
            ReportedAtUtc = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Guarantor confirms the repayment. Only a pending repayment can be
    /// confirmed; this is the guard that makes confirmation idempotent
    /// (a retry can never double-post, AGENTS §2.7/§7).
    /// </summary>
    public void Confirm(string? note = null)
    {
        if (Status != PendingRepaymentStatus.PendingConfirmation)
        {
            throw new InvalidRepaymentException("Only a pending repayment can be confirmed.");
        }

        if (note is not null && note.Length > 1000)
        {
            throw new InvalidRepaymentException("Confirmation note cannot exceed 1000 characters.");
        }

        ConfirmationNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Status = PendingRepaymentStatus.Confirmed;
        ConfirmedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Guarantor rejects the repayment. A rejected repayment never posts to any
    /// ledger.
    /// </summary>
    public void Reject(string? reason)
    {
        if (Status != PendingRepaymentStatus.PendingConfirmation)
        {
            throw new InvalidRepaymentException("Only a pending repayment can be rejected.");
        }

        Status = PendingRepaymentStatus.Rejected;
        RejectionReason = reason;
        RejectedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>State of a manually declared repayment.</summary>
public enum PendingRepaymentStatus
{
    /// <summary>Member reported the payment; awaiting Guarantor confirmation.</summary>
    PendingConfirmation = 1,

    /// <summary>Guarantor confirmed the payment; it has been posted to the ledger.</summary>
    Confirmed = 2,

    /// <summary>Guarantor rejected the payment; it never affected the ledger.</summary>
    Rejected = 3,
}
