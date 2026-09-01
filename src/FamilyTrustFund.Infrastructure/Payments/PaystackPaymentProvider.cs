using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FamilyTrustFund.Application.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyTrustFund.Infrastructure.Payments;

/// <summary>
/// Paystack implementation of <see cref="IPaymentProvider"/>.
/// </summary>
/// <remarks>
/// Confirms the provider does not mutate loan/domain state. It only creates
/// recipients and initiates transfers; final disbursement status always comes
/// from a Paystack webhook. Amounts are converted to minor units (kobo) at the
/// provider boundary; this adapter is the only place aware of that unit.
/// </remarks>
public class PaystackPaymentProvider : IPaymentProvider
{
    public const string ProviderName = "Paystack";

    private readonly HttpClient _http;
    private readonly PaystackOptions _options;
    private readonly ILogger<PaystackPaymentProvider> _logger;

    public PaystackPaymentProvider(
        HttpClient http,
        IOptions<PaystackOptions> options,
        ILogger<PaystackPaymentProvider> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => ProviderName;

    public Task<PaymentProviderResult> CreateRecipientAsync(
        CreateRecipientRequest request,
        CancellationToken ct = default) =>
        TryAsync(async () =>
        {
            var body = new
            {
                type = "nuban",
                name = request.AccountName,
                account_number = request.AccountNumber,
                bank_code = request.BankCode,
                currency = "NGN",
            };

            using var response = await PostJsonAsync("/transferrecipient", body, ct);
            using var payload = await ReadPayloadAsync(response, ct);
            if (!response.IsSuccessStatusCode || payload.RootElement.TryGetProperty("status", out var ok) && ok.GetBoolean() == false)
            {
                return PaymentProviderResult.Fail(ExtractMessage(payload));
            }

            var recipientCode = GetString(payload, "data", "recipient_code");
            return string.IsNullOrWhiteSpace(recipientCode)
                ? PaymentProviderResult.Fail("Provider did not return a recipient code.")
                : PaymentProviderResult.Ok(recipientCode);
        });

    public Task<PaymentProviderResult> InitiateTransferAsync(
        InitiateTransferRequest request,
        CancellationToken ct = default) =>
        TryAsync(async () =>
        {
            var body = new
            {
                source = "balance",
                amount = ToMinorUnits(request.Amount),
                recipient = request.ProviderRecipientCode,
                reference = request.Reference,
                reason = request.Reason,
                currency = request.Currency,
            };

            using var response = await PostJsonAsync("/transfer", body, ct);
            using var payload = await ReadPayloadAsync(response, ct);
            if (!response.IsSuccessStatusCode || payload.RootElement.TryGetProperty("status", out var ok) && ok.GetBoolean() == false)
            {
                return PaymentProviderResult.Fail(ExtractMessage(payload));
            }

            var reference = GetString(payload, "data", "reference")
                ?? GetString(payload, "data", "transfer_code");
            return string.IsNullOrWhiteSpace(reference)
                ? PaymentProviderResult.Fail("Provider did not return a transfer reference.")
                : PaymentProviderResult.Ok(reference);
        });

    /// <summary>
    /// Verifies a Paystack webhook signature (hmac sha512 of the raw body using
    /// the secret key). Returns true only for a valid signature.
    /// </summary>
    public bool VerifyWebhookSignature(string rawBody, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrEmpty(_options.SecretKey))
        {
            return false;
        }

        var keyBytes = Encoding.UTF8.GetBytes(_options.SecretKey);
        using var hmac = new HMACSHA512(keyBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        var expected = Convert.ToHexString(hash).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signatureHeader));
    }

    /// <summary>Converts NGN (decimal) to kobo (minor units) for the provider.</summary>
    public static long ToMinorUnits(decimal amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");
        }

        return (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
    }

    private async Task<PaymentProviderResult> TryAsync(Func<Task<PaymentProviderResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Paystack provider request failed.");
            return PaymentProviderResult.Fail("The payment provider could not be reached. Please try again.");
        }
    }

    private void ConfigureAuth() =>
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.SecretKey);

    private async Task<HttpResponseMessage> PostJsonAsync(
        string path,
        object body,
        CancellationToken ct)
    {
        ConfigureAuth();
        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _http.PostAsync($"{_options.BaseUrl.TrimEnd('/')}{path}", content, ct);
    }

    private static async Task<JsonDocument> ReadPayloadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return JsonDocument.Parse("{}");
        }

        try
        {
            return JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}");
        }
    }

    private static string? GetString(JsonDocument doc, params string[] path)
    {
        var element = doc.RootElement;
        foreach (var key in path)
        {
            if (!element.TryGetProperty(key, out element))
            {
                return null;
            }
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
    }

    private static string ExtractMessage(JsonDocument payload)
    {
        var msg = GetString(payload, "message");
        return string.IsNullOrWhiteSpace(msg) ? "Provider request failed." : msg!;
    }
}
