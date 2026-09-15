namespace FamilyTrustFund.Domain.Payments;

/// <summary>
/// A Guarantor capital payment for a single loan disbursement (ADR-044).
/// The Guarantor does not pre-fund the platform: they pay only when a loan is
/// disbursed, and the payment is tied to that loan. Also known as a
/// "pay per disbursement" payment.
/// </summary>
/// <remarks>
/// The Guarantor pays the gross amount (approved amount + provider fee). The
/// collection is split so the member's subaccount receives the approved amount
/// (the platform keeps the fee). The provider's collection verification/webhook
/// is authoritative; a successful API request alone never confirms a payment.
/// Provider events are idempotent. This is kept separate from committed capital
/// (ADR-002), which remains a planning figure, not deposited money.
/// </remarks>
public class CapitalTransaction
{
    public Guid Id { get; private set; }

    /// <summary>The fund whose loan this capital payment funds.</summary>
    public Guid FundId { get; private set; }

    /// <summary>The loan being disbursed; the payment funds exactly this loan.</summary>
    public Guid LoanId { get; private set; }

    /// <summary>The Guarantor who paid the money.</summary>
    public Guid GuarantorId { get; private set; }

    /// <summary>Provider identifier (e.g. "Paystack"). Not a hard-coded domain enum.</summary>
    public string Provider { get; private set; } = string.Empty;

    public CapitalTransactionStatus Status { get; private set; } = CapitalTransactionStatus.PendingConfirmation;

    /// <summary>Amount the Guarantor paid (gross, before provider fee).</summary>
    public decimal AmountGross { get; private set; }

    /// <summary>Provider-reported fee deducted from the collection (kobo-converted).</summary>
    public decimal? ProviderFee { get; private set; }

    /// <summary>Amount actually available to disburse after the fee (gross - fee).</summary>
    public decimal? AmountNet { get; private set; }

    /// <summary>Provider-issued transaction reference for the collection.</summary>
    public string ProviderReference { get; private set; } = string.Empty;

    /// <summary>Provider-hosted checkout URL the Guarantor completes. Stored so a payment can be resumed.</summary>
    public string? AuthorizationUrl { get; private set; }

    /// <summary>
    /// The provider event identifier last processed for this transaction
    /// (e.g. the Paystack webhook event id). Used to reject duplicates.
    /// </summary>
    public string? LastProcessedEventId { get; private set; }

    /// <summary>Human/machine-readable detail from the provider on failure.</summary>
    public string? FailureReason { get; private set; }

    public DateTime InitiatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    protected CapitalTransaction()
    {
    }

    /// <summary>
    /// Creates a per-loan capital payment in the PendingConfirmation state,
    /// before the Guarantor completes the provider collection.
    /// </summary>
    public static CapitalTransaction Create(
        Guid fundId,
        Guid loanId,
        Guid guarantorId,
        string provider,
        decimal amountGross,
        string providerReference)
    {
        if (fundId == Guid.Empty)
        {
            throw new InvalidPaymentException("A fund is required for a capital payment.");
        }

        if (loanId == Guid.Empty)
        {
            throw new InvalidPaymentException("A loan is required for a capital payment.");
        }

        if (guarantorId == Guid.Empty)
        {
            throw new InvalidPaymentException("A guarantor is required for a capital payment.");
        }

        if (amountGross <= 0)
        {
            throw new InvalidPaymentException("Capital payment amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new InvalidPaymentException("Payment provider is required.");
        }

        if (string.IsNullOrWhiteSpace(providerReference))
        {
            throw new InvalidPaymentException("Provider reference is required.");
        }

        var fee = ComputeEstimatedFee(amountGross);
        return new CapitalTransaction
        {
            Id = Guid.NewGuid(),
            FundId = fundId,
            LoanId = loanId,
            GuarantorId = guarantorId,
            Provider = provider.Trim(),
            AmountGross = amountGross,
            ProviderFee = fee,
            AmountNet = amountGross - fee,
            ProviderReference = providerReference.Trim(),
            Status = CapitalTransactionStatus.PendingConfirmation,
            InitiatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Records the provider-hosted checkout URL for this payment so the
    /// Guarantor can resume checkout if they navigate away.
    /// </summary>
    public void SetAuthorizationUrl(string authorizationUrl)
    {
        if (string.IsNullOrWhiteSpace(authorizationUrl))
        {
            throw new InvalidPaymentException("Authorization URL is required.");
        }

        AuthorizationUrl = authorizationUrl.Trim();
        Touch();
    }

    /// <summary>
    /// Applies a provider event to this transaction. Idempotent: if the event
    /// has already been processed it is ignored; a retry never alters the
    /// state twice. A success confirms the payment and records the provider's
    /// authoritative fee so the net amount becomes available. A failure marks
    /// the payment failed.
    /// </summary>
    /// <returns>True if the event was new and applied; false if it was a duplicate.</returns>
    public bool ApplyProviderEvent(string eventId, bool success, decimal? providerFee, string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new InvalidPaymentException("A provider event identifier is required.");
        }

        if (LastProcessedEventId == eventId)
        {
            return false;
        }

        // Terminal states are never re-written: a confirmed payment stays
        // confirmed and a failed payment stays failed regardless of later
        // (out-of-order) provider events.
        if (Status is CapitalTransactionStatus.Confirmed or CapitalTransactionStatus.Failed)
        {
            return false;
        }

        if (success)
        {
            ProviderFee = providerFee is >= 0 ? providerFee : 0m;
            AmountNet = AmountGross - (ProviderFee ?? 0m);
            Status = CapitalTransactionStatus.Confirmed;
            FailureReason = null;
            CompletedAtUtc = DateTime.UtcNow;
        }
        else
        {
            // A failed payment received nothing: clear the estimated values so
            // the record no longer suggests the Guarantor paid for the loan.
            ProviderFee = null;
            AmountNet = null;
            Status = CapitalTransactionStatus.Failed;
            FailureReason = detail;
            CompletedAtUtc = DateTime.UtcNow;
        }

        LastProcessedEventId = eventId;
        Touch();
        return true;
    }

    /// <summary>
    /// Estimates the provider fee for a gross amount using the default NGN
    /// schedule (1.5% + ₦100, capped at ₦2,000; ₦100 waived under ₦2,500).
    /// Used only for display; the provider's fee at confirmation is authoritative.
    /// </summary>
    public static decimal ComputeEstimatedFee(decimal amountGross)
    {
        if (amountGross <= 0)
        {
            return 0m;
        }

        const decimal localFeeRate = 0.015m;
        const decimal localFeeFlat = 100m;
        const decimal localFeeCap = 2000m;
        const decimal waiverThreshold = 2500m;

        var fee = decimal.Round(amountGross * localFeeRate, 2, MidpointRounding.AwayFromZero);
        if (amountGross >= waiverThreshold)
        {
            fee += localFeeFlat;
        }

        return decimal.Min(fee, localFeeCap);
    }

    /// <summary>
    /// Computes the gross amount a payer must pay so that, after the provider
    /// fee, exactly <paramref name="net"/> remains: gross - fee(gross) = net.
    /// This is the inverse of <see cref="ComputeEstimatedFee"/>, which the
    /// provider applies to the gross at charge time. Used to size a Guarantor's
    /// per-disbursement collection so the member's subaccount split settles the
    /// approved amount exactly (ADR-044). Solves at minor-unit precision.
    /// </summary>
    public static decimal ComputeGrossForNet(decimal net)
    {
        if (net <= 0)
        {
            throw new InvalidPaymentException("The net amount must be greater than zero.");
        }

        // Fees are charged on the gross and capped at ₦2,000, so the gross is
        // never more than the net plus the cap. Work in kobo (minor units) so
        // the solved gross nets the target to the nearest kobo.
        var targetKobo = decimal.ToInt64(decimal.Round(net * 100m, 0, MidpointRounding.AwayFromZero));
        long low = targetKobo;
        long high = targetKobo + 200_000L;

        while (low < high)
        {
            var mid = low + (high - low) / 2;
            var fee = ComputeEstimatedFee(mid / 100m);
            var feeKobo = decimal.ToInt64(decimal.Round(fee * 100m, 0, MidpointRounding.AwayFromZero));
            if (mid - feeKobo >= targetKobo)
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        return low / 100m;
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}