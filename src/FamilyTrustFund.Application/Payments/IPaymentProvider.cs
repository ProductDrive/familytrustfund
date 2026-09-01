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
    /// Initiates a transfer to a previously-created recipient. Sending this
    /// request does <b>not</b> establish final status — confirmation comes from
    /// a provider webhook.
    /// </summary>
    Task<PaymentProviderResult> InitiateTransferAsync(
        InitiateTransferRequest request,
        CancellationToken ct = default);
}
