namespace FamilyTrustFund.Domain.Loans;

/// <summary>
/// A loan within a fund. A loan belongs to exactly one fund and one member.
/// Approved terms are immutable historical records — the system must never
/// silently mutate them after approval.
/// </summary>
/// <remarks>
/// The loan tracks its current outstanding balance for performance, but the
/// authoritative source of truth for financial movements is the transaction
/// ledger. The balance is updated atomically whenever a financial event is
/// posted.
/// </remarks>
public class Loan
{
    public Guid Id { get; private set; }
    public Guid FundId { get; private set; }
    public Guid MemberId { get; private set; }
    public Guid? GuarantorId { get; private set; }

    /// <summary>Amount the member requested.</summary>
    public decimal RequestedAmount { get; private set; }

    /// <summary>Amount the Guarantor approved (may differ from request).</summary>
    public decimal? ApprovedAmount { get; private set; }

    /// <summary>Annual interest rate applied to this loan (External funds only).</summary>
    public decimal InterestRate { get; private set; }

    /// <summary>Repayment frequency chosen by the member at request time.</summary>
    public RepaymentFrequency RequestedFrequency { get; private set; }

    /// <summary>Repayment frequency after Guarantor approval (may differ from request).</summary>
    public RepaymentFrequency? ApprovedFrequency { get; private set; }

    /// <summary>Loan term in months requested by the member.</summary>
    public int? RequestedTermMonths { get; private set; }

    /// <summary>Number of instalments requested by the member.</summary>
    public int? RequestedInstalments { get; private set; }

    /// <summary>Loan term in months approved by the Guarantor.</summary>
    public int? ApprovedTermMonths { get; private set; }

    /// <summary>Number of scheduled instalments for this loan, frozen at approval.</summary>
    public int RepaymentTerm { get; private set; }

    public LoanStatus Status { get; private set; }

    /// <summary>
    /// How this loan was/is funded. Set at approval time based on the fund's
    /// current capital state.
    /// </summary>
    public LoanFundingSource FundingSource { get; private set; }

    /// <summary>Current outstanding principal balance (denormalised for performance, reconciliable).</summary>
    public decimal OutstandingBalance { get; private set; }

    /// <summary>Total amount expected to be repaid including interest.</summary>
    public decimal TotalRepayable { get; private set; }

    /// <summary>Optional free-text reason or purpose.</summary>
    public string? Purpose { get; private set; }

    /// <summary>Rejection reason supplied by the Guarantor.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary>User id of whoever cancelled the loan, when applicable.</summary>
    public Guid? CancelledByUserId { get; private set; }

    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public DateTime? RejectedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// When the agreed loan terms were frozen. In a Group Guarantor fund the
    /// Fund Originator freezes the terms before the co-guarantors vote, so the
    /// loan stays <see cref="LoanStatus.Pending"/> while terms are already fixed
    /// (ADR-052). Null until terms are agreed.
    /// </summary>
    public DateTime? TermsFrozenAtUtc { get; private set; }

    /// <summary>Whether the agreed terms have been frozen for voting.</summary>
    public bool HasFrozenTerms => TermsFrozenAtUtc is not null;

    protected Loan() { }

    /// <summary>
    /// Creates a new loan request from a member.
    /// </summary>
    public static Loan Request(
        Guid fundId,
        Guid memberId,
        decimal requestedAmount,
        RepaymentFrequency frequency,
        decimal interestRate,
        LoanFundingSource fundingSource,
        string? purpose = null,
        int? termMonths = null,
        int? instalments = null)
    {
        if (requestedAmount <= 0)
        {
            throw new InvalidLoanException("Loan amount must be greater than zero.");
        }

        if (purpose is not null && purpose.Length > 1000)
        {
            throw new InvalidLoanException("Loan purpose cannot exceed 1000 characters.");
        }

        if (termMonths is <= 0)
        {
            throw new InvalidLoanException("Loan term must be greater than zero months.");
        }

        if (instalments is <= 0)
        {
            throw new InvalidLoanException("Number of instalments must be greater than zero.");
        }

        return new Loan
        {
            Id = Guid.NewGuid(),
            FundId = fundId,
            MemberId = memberId,
            RequestedAmount = requestedAmount,
            InterestRate = interestRate,
            RequestedFrequency = frequency,
            RequestedTermMonths = termMonths,
            RequestedInstalments = instalments,
            Status = LoanStatus.Pending,
            FundingSource = fundingSource,
            Purpose = purpose?.Trim(),
            RequestedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Guarantor approves the loan. The Guarantor may adjust the repayment
    /// frequency and the approved amount. Terms are frozen after this call.
    /// </summary>
    /// <remarks>
    /// Approved terms become immutable historical records (ADR-010). Do not
    /// silently mutate after approval.
    /// </remarks>
    public void Approve(
        Guid guarantorId,
        decimal approvedAmount,
        RepaymentFrequency approvedFrequency,
        decimal totalRepayable,
        int repaymentTerm = 4,
        int? approvedTermMonths = null)
    {
        FreezeApprovedTerms(
            guarantorId,
            approvedAmount,
            approvedFrequency,
            totalRepayable,
            repaymentTerm,
            approvedTermMonths);

        Status = LoanStatus.Approved;
        ApprovedAtUtc = DateTime.UtcNow;
        Touch();
    }

    /// <summary>
    /// Freezes the agreed loan terms without moving the loan out of
    /// <see cref="LoanStatus.Pending"/>. In a Group Guarantor fund the Fund
    /// Originator agrees the terms first and the loan is only approved once
    /// every active Guarantor has voted (ADR-052).
    /// </summary>
    public void FreezeApprovedTerms(
        Guid guarantorId,
        decimal approvedAmount,
        RepaymentFrequency approvedFrequency,
        decimal totalRepayable,
        int repaymentTerm = 4,
        int? approvedTermMonths = null)
    {
        if (Status != LoanStatus.Pending)
        {
            throw new InvalidLoanException("Only pending loans can have their terms agreed.");
        }

        if (HasFrozenTerms)
        {
            throw new InvalidLoanException("The loan terms have already been agreed for this request.");
        }

        if (approvedAmount <= 0)
        {
            throw new InvalidLoanException("Approved amount must be greater than zero.");
        }

        if (totalRepayable < approvedAmount)
        {
            throw new InvalidLoanException("Total repayable must be at least the approved amount.");
        }

        if (repaymentTerm <= 0)
        {
            throw new InvalidLoanException("Repayment term must be a positive number of instalments.");
        }

        if (approvedTermMonths is <= 0)
        {
            throw new InvalidLoanException("Approved loan term must be greater than zero months.");
        }

        GuarantorId = guarantorId;
        ApprovedAmount = approvedAmount;
        ApprovedFrequency = approvedFrequency;
        ApprovedTermMonths = approvedTermMonths ?? RequestedTermMonths;
        RepaymentTerm = repaymentTerm;
        TotalRepayable = totalRepayable;
        OutstandingBalance = approvedAmount;
        TermsFrozenAtUtc = DateTime.UtcNow;
        Touch();
    }

    /// <summary>
    /// Moves a loan whose terms are already frozen to
    /// <see cref="LoanStatus.Approved"/> after every required Guarantor has
    /// approved (unanimity, ADR-052).
    /// </summary>
    public void CompleteUnanimousApproval()
    {
        if (Status != LoanStatus.Pending)
        {
            throw new InvalidLoanException("Only pending loans can be approved.");
        }

        if (!HasFrozenTerms)
        {
            throw new InvalidLoanException("The loan terms must be agreed before the loan can be approved.");
        }

        Status = LoanStatus.Approved;
        ApprovedAtUtc = DateTime.UtcNow;
        Touch();
    }

    /// <summary>
    /// Guarantor rejects the loan request.
    /// </summary>
    public void Reject(Guid guarantorId, string? reason = null)
    {
        if (Status != LoanStatus.Pending)
        {
            throw new InvalidLoanException("Only pending loans can be rejected.");
        }

        if (reason is not null && reason.Length > 1000)
        {
            throw new InvalidLoanException("Rejection reason cannot exceed 1000 characters.");
        }

        GuarantorId = guarantorId;
        Status = LoanStatus.Rejected;
        RejectionReason = reason?.Trim();
        RejectedAtUtc = DateTime.UtcNow;
        Touch();
    }

    /// <summary>
    /// A member cancels their own loan request while it is still pending.
    /// A pending request may be withdrawn before the Guarantor decides.
    /// </summary>
    public void CancelByMember(Guid memberId)
    {
        if (Status != LoanStatus.Pending)
        {
            throw new InvalidLoanException("You can only cancel a pending loan request.");
        }

        CancelledByUserId = memberId;
        Status = LoanStatus.Cancelled;
        CancelledAtUtc = DateTime.UtcNow;
        Touch();
    }

    /// <summary>
    /// The Guarantor cancels a loan that has not yet been disbursed —
    /// pending, approved, or disbursement-pending (e.g. failed/incomplete
    /// disbursement). A cancellation is a permanent terminal state; disbursed
    /// or completed loans can never be cancelled.
    /// </summary>
    public void CancelByGuarantor(Guid guarantorId)
    {
        if (Status != LoanStatus.Pending && Status != LoanStatus.Approved && Status != LoanStatus.DisbursementPending)
        {
            throw new InvalidLoanException("Only pending, approved, or disbursement-pending (not yet disbursed) loans can be cancelled.");
        }

        CancelledByUserId = guarantorId;
        Status = LoanStatus.Cancelled;
        CancelledAtUtc = DateTime.UtcNow;
        Touch();
    }

    /// <summary>
    /// Marks disbursement as initiated. The loan is not yet DISBURSED —
    /// provider confirmation via webhook is required (ADR-015).
    /// </summary>
    public void MarkDisbursementPending()
    {
        if (Status != LoanStatus.Approved)
        {
            throw new InvalidLoanException("Only approved loans can be sent for disbursement.");
        }

        Status = LoanStatus.DisbursementPending;
        Touch();
    }

    /// <summary>
    /// Confirms successful disbursement after provider webhook confirmation.
    /// Do not call this merely because the API request succeeded.
    /// </summary>
    public void MarkDisbursed()
    {
        if (Status != LoanStatus.DisbursementPending)
        {
            throw new InvalidLoanException("Only disbursement-pending loans can be marked as disbursed.");
        }

        Status = LoanStatus.Disbursed;
        Touch();
    }

    /// <summary>
    /// Marks the loan as completed (fully repaid or settled).
    /// </summary>
    public void MarkCompleted()
    {
        if (Status != LoanStatus.Disbursed)
        {
            throw new InvalidLoanException("Only disbursed loans can be completed.");
        }

        Status = LoanStatus.Completed;
        OutstandingBalance = 0;
        Touch();
    }

    /// <summary>
    /// Marks the loan as defaulted.
    /// </summary>
    public void MarkDefaulted()
    {
        if (Status != LoanStatus.Disbursed)
        {
            throw new InvalidLoanException("Only disbursed loans can be defaulted.");
        }

        Status = LoanStatus.Defaulted;
        Touch();
    }

    /// <summary>
    /// Updates the outstanding balance after a confirmed repayment.
    /// </summary>
    public void ReduceOutstandingBalance(decimal amount)
    {
        if (amount <= 0)
        {
            throw new InvalidLoanException("Repayment amount must be greater than zero.");
        }

        if (OutstandingBalance < amount)
        {
            throw new InvalidLoanException("Repayment amount exceeds outstanding balance.");
        }

        OutstandingBalance -= amount;
        Touch();
    }

    /// <summary>
    /// Whether the loan is in a state that prevents the member from making
    /// contributions or requesting another loan.
    /// </summary>
    public bool IsActiveDisbursed => Status == LoanStatus.Disbursed;

    /// <summary>
    /// Whether the loan is still awaiting Guarantor decision.
    /// </summary>
    public bool IsPending => Status == LoanStatus.Pending;

    /// <summary>
    /// Whether the loan has been rejected by the Guarantor.
    /// </summary>
    public bool IsRejected => Status == LoanStatus.Rejected;

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
