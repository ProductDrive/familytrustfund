namespace FamilyTrustFund.Infrastructure.Payments;

/// <summary>
/// Configuration for the Paystack payment provider. Read from
/// configuration (e.g. "PaymentProviders:Paystack:SecretKey"). Never hard-coded
/// and never written to logs.
/// </summary>
public sealed class PaystackOptions
{
    public const string SectionName = "PaymentProviders:Paystack";
    public string SecretKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.paystack.co";
}
