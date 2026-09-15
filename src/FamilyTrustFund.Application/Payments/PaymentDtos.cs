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

/// <summary>
/// Request to create a provider subaccount (used to settle disbursements to a
/// member's bank account after the Guarantor's payment is confirmed).
/// </summary>
public sealed class CreateSubaccountRequest
{
    public string Provider { get; init; } = string.Empty;
    public string BankCode { get; init; } = string.Empty;
    public string AccountNumber { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public string Currency { get; init; } = "NGN";
}

/// <summary>
/// Request to start a collection from a Guarantor. When a subaccount is
/// supplied the collection is split so the subaccount (the member's) receives
/// its share and the platform keeps the configured transaction charge.
/// </summary>
public sealed class CollectionInitiationRequest
{
    public string Provider { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "NGN";
    public string Reference { get; init; } = string.Empty;
    public string? CallbackUrl { get; init; }

    /// <summary>Provider subaccount code the payment is split to (e.g. the member's).</summary>
    public string? SubaccountCode { get; init; }

    /// <summary>
    /// Flat amount the platform account receives from the collection regardless
    /// of the split (covers the provider fee). The balance settles to the
    /// subaccount.
    /// </summary>
    public decimal? TransactionCharge { get; init; }

    /// <summary>Who bears provider charges: "account" or "subaccount". Optional; provider-defaulted.</summary>
    public string? Bearer { get; init; }
}

/// <summary>
/// Provider-neutral result of a collection initiation.
/// </summary>
public sealed class CollectionInitiationResult
{
    public bool Success { get; init; }
    public string? RejectionReason { get; init; }

    /// <summary>Provider-issued transaction reference for the collection.</summary>
    public string? ProviderReference { get; init; }

    /// <summary>Provider-hosted checkout URL to redirect the payer to.</summary>
    public string? AuthorizationUrl { get; init; }

    public static CollectionInitiationResult Ok(string providerReference, string authorizationUrl) =>
        new() { Success = true, ProviderReference = providerReference, AuthorizationUrl = authorizationUrl };

    public static CollectionInitiationResult Fail(string reason) =>
        new() { Success = false, RejectionReason = reason };
}

/// <summary>
/// Provider estimate for a collection charge.
/// </summary>
public sealed class CollectionChargeEstimateResult
{
    public bool Success { get; init; }
    public string? RejectionReason { get; init; }

    /// <summary>Provider-estimated fee for the requested amount.</summary>
    public decimal EstimatedFee { get; init; }

    /// <summary>Gross amount the payer must pay so the net reaches the target.</summary>
    public decimal GrossAmount { get; init; }

    public static CollectionChargeEstimateResult Ok(decimal estimatedFee, decimal grossAmount) =>
        new() { Success = true, EstimatedFee = estimatedFee, GrossAmount = grossAmount };

    public static CollectionChargeEstimateResult Fail(string reason) =>
        new() { Success = false, RejectionReason = reason };
}

/// <summary>
/// Provider-neutral result of a collection verification.
/// </summary>
public sealed class CollectionVerificationResult
{
    public bool Success { get; init; }
    public string? RejectionReason { get; init; }

    public bool Paid { get; init; }

    /// <summary>Amount the payer paid (gross), re-based to the domain currency.</summary>
    public decimal AmountPaid { get; init; }

    /// <summary>Provider fee charged for the collection, re-based to the domain currency.</summary>
    public decimal ProviderFee { get; init; }

    public static CollectionVerificationResult PaidSuccess(decimal amountPaid, decimal providerFee) =>
        new() { Success = true, Paid = true, AmountPaid = amountPaid, ProviderFee = providerFee };

    public static CollectionVerificationResult NotPaid(string reason) =>
        new() { Success = true, Paid = false, RejectionReason = reason };

    public static CollectionVerificationResult Fail(string reason) =>
        new() { Success = false, RejectionReason = reason };
}
