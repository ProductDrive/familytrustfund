namespace FamilyTrustFund.Domain.Payments;

/// <summary>
/// A single disbursement settlement from the fund to a member's recipient.
/// </summary>
/// <remarks>
/// This records the authoritative provider transaction for a loan disbursement.
/// The Guarantor pays per disbursement (ADR-044); the provider reference is the
/// collection the Guarantor must complete. The member is settled through their
/// subaccount once the collection is confirmed. Provider events are idempotent:
/// a duplicate webhook must never alter state twice. The loan itself is only
/// marked DISBURSED after a provider-confirmed success (AGENTS §3).
/// </remarks>
public class DisbursementTransaction
{
    public Guid Id { get; private set; }
    public Guid LoanId { get; private set; }

    /// <summary>
    /// The payment recipient used for this transfer, preserved at the time of
    /// disbursement so historical destinations are never overwritten.
    /// </summary>
    public Guid RecipientId { get; private set; }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "NGN";

    /// <summary>Provider-issued transaction/transfer reference.</summary>
    public string ProviderReference { get; private set; } = string.Empty;

    /// <summary>Application-generated idempotency key for retry safety.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>
    /// The provider event identifier last processed for this transaction
    /// (e.g. the Paystack webhook event id). Used to reject duplicates.
    /// </summary>
    public string? LastProcessedEventId { get; private set; }

    public DisbursementStatus Status { get; private set; } = DisbursementStatus.Pending;

    /// <summary>Human/machine-readable detail from the provider on failure.</summary>
    public string? FailureReason { get; private set; }

    public DateTime InitiatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    protected DisbursementTransaction()
    {
    }

    /// <summary>
    /// Creates a disbursement in the Pending state.
    /// </summary>
    public static DisbursementTransaction Initiate(
        Guid loanId,
        Guid recipientId,
        decimal amount,
        string providerReference,
        string idempotencyKey,
        string currency = "NGN")
    {
        if (loanId == Guid.Empty)
        {
            throw new InvalidPaymentException("A loan is required for a disbursement.");
        }

        if (recipientId == Guid.Empty)
        {
            throw new InvalidPaymentException("A payment recipient is required for a disbursement.");
        }

        if (amount <= 0)
        {
            throw new InvalidPaymentException("Disbursement amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(providerReference))
        {
            throw new InvalidPaymentException("Provider reference is required.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidPaymentException("An idempotency key is required.");
        }

        return new DisbursementTransaction
        {
            Id = Guid.NewGuid(),
            LoanId = loanId,
            RecipientId = recipientId,
            Amount = amount,
            Currency = currency,
            ProviderReference = providerReference.Trim(),
            IdempotencyKey = idempotencyKey.Trim(),
            Status = DisbursementStatus.Pending,
            InitiatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Applies a provider event to this transaction. Idempotent: if the event
    /// has already been processed it is ignored; a retry never alters the
    /// state twice.
    /// </summary>
    /// <returns>True if the event was new and applied; false if it was a duplicate.</returns>
    public bool ApplyProviderEvent(string eventId, DisbursementStatus status, string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new InvalidPaymentException("A provider event identifier is required.");
        }

        if (LastProcessedEventId == eventId)
        {
            return false;
        }

        Status = status;
        LastProcessedEventId = eventId;
        FailureReason = status == DisbursementStatus.Successful ? null : detail;
        if (status == DisbursementStatus.Successful
            || status == DisbursementStatus.Failed
            || status == DisbursementStatus.Reversed)
        {
            CompletedAtUtc = DateTime.UtcNow;
        }

        Touch();
        return true;
    }

    /// <summary>
    /// Re-opens a failed disbursement for a new Guarantor payment attempt,
    /// replacing the provider reference and resetting state to Pending.
    /// Historical audit records keep the previous attempt.
    /// </summary>
    public void ResetForRetry(string providerReference, string idempotencyKey)
    {
        if (Status != DisbursementStatus.Failed)
        {
            throw new InvalidPaymentException("Only a failed disbursement can be retried.");
        }

        if (string.IsNullOrWhiteSpace(providerReference))
        {
            throw new InvalidPaymentException("Provider reference is required.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidPaymentException("An idempotency key is required.");
        }

        ProviderReference = providerReference.Trim();
        IdempotencyKey = idempotencyKey.Trim();
        LastProcessedEventId = null;
        Status = DisbursementStatus.Pending;
        FailureReason = null;
        CompletedAtUtc = null;
        Touch();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
