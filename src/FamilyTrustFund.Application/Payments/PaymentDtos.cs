namespace FamilyTrustFund.Application.Payments;

/// <summary>
/// Request to create (verify) a payment recipient with the provider.
/// </summary>
public sealed class CreateRecipientRequest
{
    public string Provider { get; init; } = string.Empty;
    public string BankCode { get; init; } = string.Empty;
    public string AccountNumber { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
}

/// <summary>
/// Request to initiate a transfer to an existing provider recipient.
/// </summary>
public sealed class InitiateTransferRequest
{
    public string Provider { get; init; } = string.Empty;
    public string ProviderRecipientCode { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "NGN";
    public string Reference { get; init; } = string.Empty;

    /// <summary>Application-generated idempotency key (e.g. a GUID).</summary>
    public string IdempotencyKey { get; init; } = string.Empty;
    public string Reason { get; init; } = "Loan disbursement";
}

/// <summary>
/// Provider-neutral result of a recipient creation or transfer initiation.
/// </summary>
public sealed class PaymentProviderResult
{
    public bool Success { get; init; }
    public string? RejectionReason { get; init; }

    /// <summary>Provider-issued recipient/transfer code or reference on success.</summary>
    public string? ProviderReference { get; init; }

    public static PaymentProviderResult Ok(string providerReference) =>
        new() { Success = true, ProviderReference = providerReference };

    public static PaymentProviderResult Fail(string reason) =>
        new() { Success = false, RejectionReason = reason };
}
