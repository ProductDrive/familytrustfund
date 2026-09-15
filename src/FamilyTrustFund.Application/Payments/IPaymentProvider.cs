namespace FamilyTrustFund.Application.Payments;

/// <summary>
/// A provider-neutral payment/transfer abstraction. The domain and application
/// layers never depend directly on a concrete payment provider (AGENTS §3).
/// </summary>
/// <remarks>
/// Implementations are registered by name (e.g. "Paystack"). The application
/// selects an implementation through the registry so a future Flutterwave
/// provider can be added without changing the loan/fund/domain code.
/// </remarks>
public interface IPaymentProvider
{
    /// <summary>Stable provider identifier, e.g. "Paystack".</summary>
    string Name { get; }

    /// <summary>
    /// Resolves/verifies bank details and creates a provider-side recipient.
    /// </summary>
    Task<PaymentProviderResult> CreateRecipientAsync(
        CreateRecipientRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a provider subaccount that disbursements settle to. Used for the
    /// member's bank details so the member is paid through their subaccount
    /// after the Guarantor's payment is confirmed (ADR-044).
    /// </summary>
    Task<PaymentProviderResult> CreateSubaccountAsync(
        CreateSubaccountRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Initiates a transfer to a previously-created recipient. Sending this
    /// request does <b>not</b> establish final status — confirmation comes from
    /// a provider webhook.
    /// </summary>
    Task<PaymentProviderResult> InitiateTransferAsync(
        InitiateTransferRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Estimates the fee and gross amount the payer must pay so that roughly
    /// <paramref name="amount"/> is available after provider charges. Used only
    /// for display; the provider's fee at confirmation is authoritative.
    /// </summary>
    Task<CollectionChargeEstimateResult> EstimateCollectionChargeAsync(
        decimal amount,
        string currency,
        CancellationToken ct = default);

    /// <summary>
    /// Initializes a collection (money-in) from a Guarantor into the platform
    /// account. Returns a provider checkout URL the Guarantor is redirected to.
    /// Sending this request does <b>not</b> establish payment — confirmation
    /// comes from a provider webhook or verification.
    /// </summary>
    Task<CollectionInitiationResult> InitializeCollectionAsync(
        CollectionInitiationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Verifies a collection by its provider reference. The provider is
    /// authoritative for whether the payment succeeded, the amount paid and the
    /// fee charged.
    /// </summary>
    Task<CollectionVerificationResult> VerifyCollectionAsync(
        string providerReference,
        CancellationToken ct = default);
}
