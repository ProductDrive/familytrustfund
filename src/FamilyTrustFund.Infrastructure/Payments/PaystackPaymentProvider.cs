using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FamilyTrustFund.Application.Payments;
using FamilyTrustFund.Domain.Payments;
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

    public Task<PaymentProviderResult> CreateSubaccountAsync(
        CreateSubaccountRequest request,
        CancellationToken ct = default) =>
        TryAsync(async () =>
        {
            var body = new
            {
                business_name = request.AccountName,
                settlement_bank = request.BankCode,
                account_number = request.AccountNumber,
                percentage_charge = 0m,
                settlement_schedule = "auto",
                currency = request.Currency,
            };

            using var response = await PostJsonAsync("/subaccount", body, ct);
            using var payload = await ReadPayloadAsync(response, ct);
            if (!response.IsSuccessStatusCode || payload.RootElement.TryGetProperty("status", out var ok) && ok.GetBoolean() == false)
            {
                return PaymentProviderResult.Fail(ExtractMessage(payload));
            }

            var subaccountCode = GetString(payload, "data", "subaccount_code");
            return string.IsNullOrWhiteSpace(subaccountCode)
                ? PaymentProviderResult.Fail("Provider did not return a subaccount code.")
                : PaymentProviderResult.Ok(subaccountCode);
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

    public Task<CollectionChargeEstimateResult> EstimateCollectionChargeAsync(
        decimal amount,
        string currency,
        CancellationToken ct = default) =>
        TryEstimateAsync(amount, async () =>
        {
            var body = new { amount = ToMinorUnits(amount), currency = currency };
            using var response = await PostJsonAsync("/charge_estimate", body, ct);
            using var payload = await ReadPayloadAsync(response, ct);
            if (!response.IsSuccessStatusCode || payload.RootElement.TryGetProperty("status", out var ok) && ok.GetBoolean() == false)
            {
                // Provider estimate unavailable — fall back to a local estimate so
                // the Guarantor can still see what they will pay. The provider's
                // fee at confirmation is authoritative.
                return EstimateFallback(amount);
            }

            var data = payload.RootElement.TryGetProperty("data", out var d) ? d : default;
            var fees = GetLong(data, "fees");
            var effective = GetLong(data, "effective_amount");

            if (effective is not null)
            {
                var gross = FromMinorUnits(effective.Value);
                return CollectionChargeEstimateResult.Ok(gross - amount, gross);
            }

            if (fees is not null)
            {
                return CollectionChargeEstimateResult.Ok(FromMinorUnits(fees.Value), amount + FromMinorUnits(fees.Value));
            }

            return EstimateFallback(amount);
        });

    public Task<CollectionInitiationResult> InitializeCollectionAsync(
        CollectionInitiationRequest request,
        CancellationToken ct = default) =>
        TryInitiateCollectionAsync(async () =>
        {
            var body = new
            {
                email = request.Email,
                amount = ToMinorUnits(request.Amount),
                currency = request.Currency,
                reference = request.Reference,
                callback_url = request.CallbackUrl,
                subaccount = request.SubaccountCode,
                transaction_charge = request.TransactionCharge.HasValue ? ToMinorUnits(request.TransactionCharge.Value) : (long?)null,
                bearer = request.Bearer,
            };

            using var response = await PostJsonAsync("/transaction/initialize", body, ct);
            using var payload = await ReadPayloadAsync(response, ct);
            if (!response.IsSuccessStatusCode || payload.RootElement.TryGetProperty("status", out var ok) && ok.GetBoolean() == false)
            {
                return CollectionInitiationResult.Fail(ExtractMessage(payload));
            }

            var reference = GetString(payload, "data", "reference") ?? request.Reference;
            var authorizationUrl = GetString(payload, "data", "authorization_url");
            return string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(authorizationUrl)
                ? CollectionInitiationResult.Fail("Provider did not return a checkout URL.")
                : CollectionInitiationResult.Ok(reference, authorizationUrl);
        });

    public Task<CollectionVerificationResult> VerifyCollectionAsync(
        string providerReference,
        CancellationToken ct = default) =>
        TryVerifyCollectionAsync(async () =>
        {
            ConfigureAuth();
            using var response = await _http.GetAsync($"{_options.BaseUrl.TrimEnd('/')}/transaction/verify/{Uri.EscapeDataString(providerReference)}", ct);
            using var payload = await ReadPayloadAsync(response, ct);
            if (!response.IsSuccessStatusCode || payload.RootElement.TryGetProperty("status", out var ok) && ok.GetBoolean() == false)
            {
                return CollectionVerificationResult.Fail(ExtractMessage(payload));
            }

            var status = GetString(payload, "data", "status");
            if (!string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
            {
                return CollectionVerificationResult.NotPaid($"Payment status is '{status}'.");
            }

            var amountKobo = GetLong(payload.RootElement, "data", "amount") ?? 0;
            var feeKobo = GetLong(payload.RootElement, "data", "fees") ?? 0;
            return CollectionVerificationResult.PaidSuccess(FromMinorUnits(amountKobo), FromMinorUnits(feeKobo));
        });

    private static CollectionChargeEstimateResult EstimateFallback(decimal amount)
    {
        var fee = CapitalTransaction.ComputeEstimatedFee(amount);
        return CollectionChargeEstimateResult.Ok(fee, amount + fee);
    }

    /// <summary>Converts kobo (minor units) to NGN (decimal).</summary>
    public static decimal FromMinorUnits(long amount) => amount / 100m;

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

    private async Task<CollectionChargeEstimateResult> TryEstimateAsync(decimal amount, Func<Task<CollectionChargeEstimateResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Paystack charge estimate request failed; using local estimate.");
            return EstimateFallback(amount);
        }
    }

    private async Task<CollectionInitiationResult> TryInitiateCollectionAsync(Func<Task<CollectionInitiationResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Paystack collection initiation request failed.");
            return CollectionInitiationResult.Fail("The payment provider could not be reached. Please try again.");
        }
    }

    private async Task<CollectionVerificationResult> TryVerifyCollectionAsync(Func<Task<CollectionVerificationResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Paystack collection verification request failed.");
            return CollectionVerificationResult.Fail("The payment provider could not be reached. Please try again.");
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

    private static long? GetLong(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var key in path)
        {
            if (!current.TryGetProperty(key, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.Number when current.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(current.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static string ExtractMessage(JsonDocument payload)
    {
        var msg = GetString(payload, "message");
        return string.IsNullOrWhiteSpace(msg) ? "Provider request failed." : msg!;
    }
}
