namespace FamilyTrustFund.Application.Payments;

/// <summary>
/// Resolves an <see cref="IPaymentProvider"/> by provider name.
/// </summary>
public interface IPaymentProviderRegistry
{
    /// <summary>
    /// Returns the provider whose <see cref="IPaymentProvider.Name"/> matches,
    /// or null if none is registered.
    /// </summary>
    IPaymentProvider? Get(string providerName);
}
