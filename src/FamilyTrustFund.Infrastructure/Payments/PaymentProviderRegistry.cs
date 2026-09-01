using FamilyTrustFund.Application.Payments;

namespace FamilyTrustFund.Infrastructure.Payments;

/// <summary>
/// Resolves an <see cref="IPaymentProvider"/> from the set registered in DI by
/// matching provider name.
/// </summary>
public sealed class PaymentProviderRegistry : IPaymentProviderRegistry
{
    private readonly IEnumerable<IPaymentProvider> _providers;

    public PaymentProviderRegistry(IEnumerable<IPaymentProvider> providers)
    {
        _providers = providers;
    }

    public IPaymentProvider? Get(string providerName) =>
        _providers.FirstOrDefault(p =>
            string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));
}
