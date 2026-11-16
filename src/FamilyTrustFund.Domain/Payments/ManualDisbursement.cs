namespace FamilyTrustFund.Domain.Payments;

/// <summary>
/// An offline/manual loan disbursement: the Guarantor paid the member outside
/// the payment provider and reports it, and the loan only becomes DISBURSED once
/// the member confirms receipt (ADR-056).
/// </summary>
/// <remarks>
/// This is the mirror image of <see cref="FamilyTrustFund.Domain.Repayments.PendingRepayment"/>:
/// a financial action is reported by one party and only posts to the ledger once
/// the counterparty confirms it (AGENTS §2.9). Reporting never changes the
/// ledger. Member confirmation is the authoritative event and is a guarded,
/// idempotent transition so a retry can never double-disburse (AGENTS §7).
///
/// The member receives exactly the approved amount; there is no provider fee on
/// this path (the Guarantor bears their own bank charges).
/// </remarks>
public class ManualDisbursement
{
    public Guid Id { get; private set; }
    public Guid LoanId { get; private set; }
    public Guid FundId { get; private set; }
    public Guid MemberId { get; private set; }

    /// <summary>The Fund Originator who reported the offline payment.</summary>
    public Guid ReportedByGuarantorId { get; private set; }

    /// <summary>Amount the Guarantor reports having paid (must equal the approved amount).</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = "NGN";

    public ManualDisbursementMethod Method { get; private set; }

    /// <summary>Destination bank name, when paid by transfer. Never the full account number.</summary>
    public string? DestinationBankName { get; private set; }

    /// <summary>Last four digits of the destination account, when known.</summary>
    public string? DestinationAccountLast4 { get; private set; }

    /// <summary>Optional Guarantor-supplied reference (e.g. bank transfer reference).</summary>
    public string? Reference { get; private set; }

    /// <summary>Optional Guarantor note.</summary>
    public string? Note { get; private set; }

    public ManualDisbursementStatus Status { get; private set; }

    /// <summary>Optional member note supplied on confirmation or dispute.</summary>
    public string? MemberNote { get; private set; }

    /// <summary>Optional reason recorded when the Guarantor cancels the report.</summary>
    public string? CancelReason { get; private set; }

    public DateTime ReportedAtUtc { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public DateTime? DisputedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }

    protected ManualDisbursement()
    {
    }

    /// <summary>
    /// Creates a draft offline-disbursement report. The report is not yet
    /// visible to the member and does not affect the ledger.
    /// </summary>
    public static ManualDisbursement CreateDraft(
        Guid loanId,
        Guid fundId,
        Guid memberId,
        Guid reportedByGuarantorId,
        decimal amount,
        decimal approvedAmount,
        ManualDisbursementMethod method,
        string? destinationBankName = null,
        string? destinationAccountLast4 = null,
        string? reference = null,
        string? note = null)
    {
        if (loanId == Guid.Empty)
        {
            throw new InvalidPaymentException("A loan is required for an offline disbursement.");
        }

        if (fundId == Guid.Empty)
        {
            throw new InvalidPaymentException("A fund is required for an offline disbursement.");
        }

        if (memberId == Guid.Empty)
        {
            throw new InvalidPaymentException("A member is required for an offline disbursement.");
        }

        if (reportedByGuarantorId == Guid.Empty)
        {
            throw new InvalidPaymentException("A reporting Guarantor is required for an offline disbursement.");
        }

        if (amount <= 0)
        {
            throw new InvalidPaymentException("Offline disbursement amount must be greater than zero.");
        }

        // The member must receive exactly the approved amount; a report for any
        // other amount would corrupt the loan's funding record (ADR-056).
        if (decimal.Round(amount, 2, MidpointRounding.AwayFromZero)
            != decimal.Round(approvedAmount, 2, MidpointRounding.AwayFromZero))
        {
            throw new InvalidPaymentException(
                $"The reported amount must equal the approved loan amount of ₦{approvedAmount:N2}.");
        }

        if (!Enum.IsDefined(method))
        {
            throw new InvalidPaymentException("A valid offline disbursement method is required.");
        }

        if (note is not null && note.Length > 1000)
        {
            throw new InvalidPaymentException("Note cannot exceed 1000 characters.");
        }

        if (reference is not null && reference.Length > 200)
        {
            throw new InvalidPaymentException("Reference cannot exceed 200 characters.");
        }

        return new ManualDisbursement
        {
            Id = Guid.NewGuid(),
            LoanId = loanId,
            FundId = fundId,
            MemberId = memberId,
            ReportedByGuarantorId = reportedByGuarantorId,
            Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero),
            Method = method,
            DestinationBankName = string.IsNullOrWhiteSpace(destinationBankName) ? null : destinationBankName.Trim(),
            DestinationAccountLast4 = NormaliseLast4(destinationAccountLast4),
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            Status = ManualDisbursementStatus.Draft,
            ReportedAtUtc = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Whether this report is still live and therefore blocks any other
    /// disbursement attempt for the same loan. A disputed report stays live
    /// until the Guarantor resolves it, so the Paystack path can never run in
    /// parallel with an open dispute (ADR-056).
    /// </summary>
    public bool IsLive => Status is ManualDisbursementStatus.Draft
        or ManualDisbursementStatus.PendingMemberConfirmation
        or ManualDisbursementStatus.Disputed;

    /// <summary>
    /// Submits the report to the member for confirmation. Evidence must already
    /// exist; that is enforced by the application service.
    /// </summary>
    public void Submit()
    {
        if (Status != ManualDisbursementStatus.Draft)
        {
            throw new InvalidPaymentException("Only a draft offline disbursement can be submitted.");
        }

        Status = ManualDisbursementStatus.PendingMemberConfirmation;
        SubmittedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// The member confirms receipt. Only a submitted report can be confirmed;
    /// this guard makes confirmation idempotent so a retry can never
    /// double-disburse (AGENTS §7).
    /// </summary>
    public void Confirm(Guid memberId, string? note = null)
    {
        if (Status != ManualDisbursementStatus.PendingMemberConfirmation)
        {
            throw new InvalidPaymentException("Only an offline disbursement awaiting your confirmation can be confirmed.");
        }

        EnsureMember(memberId);

        if (note is not null && note.Length > 1000)
        {
            throw new InvalidPaymentException("Confirmation note cannot exceed 1000 characters.");
        }

        MemberNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Status = ManualDisbursementStatus.Confirmed;
        ConfirmedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// The member disputes receipt. The loan stays in DISBURSEMENT_PENDING and
    /// the report stays live until the Guarantor resolves it.
    /// </summary>
    public void Dispute(Guid memberId, string reason)
    {
        if (Status != ManualDisbursementStatus.PendingMemberConfirmation)
        {
            throw new InvalidPaymentException("Only an offline disbursement awaiting your confirmation can be disputed.");
        }

        EnsureMember(memberId);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidPaymentException("A reason is required to dispute an offline disbursement.");
        }

        if (reason.Length > 1000)
        {
            throw new InvalidPaymentException("Dispute reason cannot exceed 1000 characters.");
        }

        MemberNote = reason.Trim();
        Status = ManualDisbursementStatus.Disputed;
        DisputedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// The Guarantor withdraws the report. Allowed while drafting, awaiting the
    /// member, or after a dispute, so a stuck report can always be resolved and
    /// the Guarantor can retry. A confirmed report can never be cancelled.
    /// </summary>
    public void Cancel(string? reason = null)
    {
        if (Status is ManualDisbursementStatus.Confirmed or ManualDisbursementStatus.Cancelled)
        {
            throw new InvalidPaymentException("A confirmed or already-cancelled offline disbursement cannot be cancelled.");
        }

        if (reason is not null && reason.Length > 1000)
        {
            throw new InvalidPaymentException("Cancellation reason cannot exceed 1000 characters.");
        }

        CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Status = ManualDisbursementStatus.Cancelled;
        CancelledAtUtc = DateTime.UtcNow;
    }

    private void EnsureMember(Guid memberId)
    {
        if (memberId == Guid.Empty || memberId != MemberId)
        {
            throw new InvalidPaymentException("Only the member this loan belongs to can act on this disbursement.");
        }
    }

    private static string? NormaliseLast4(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits[^Math.Min(4, digits.Length)..];
    }
}
