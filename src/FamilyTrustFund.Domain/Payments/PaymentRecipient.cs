namespace FamilyTrustFund.Domain.Payments;

/// <summary>
/// A payment/transfer recipient and the bank account it represents.
/// </summary>
/// <remarks>
/// Bank/payment details are deliberately separated from ordinary profile data
/// (AGENTS §8). A loan disbursement preserves which recipient/account was used
/// at the time of the transaction. Provider-specific identifiers (such as the
/// provider recipient code) are stored here, never in core business entities.
/// The account number is treated as sensitive and must not be written to logs.
/// </remarks>
public class PaymentRecipient
{
    public Guid Id { get; private set; }
    public Guid MemberId { get; private set; }

    /// <summary>Provider identifier (e.g. "Paystack"). Not a hard-coded domain enum.</summary>
    public string Provider { get; private set; } = string.Empty;

    public string BankCode { get; private set; } = string.Empty;
    public string BankName { get; private set; } = string.Empty;
    public string AccountNumber { get; private set; } = string.Empty;
    public string AccountName { get; private set; } = string.Empty;

    /// <summary>Provider-issued recipient reference (e.g. Paystack recipient code).</summary>
    public string? ProviderRecipientCode { get; private set; }

    /// <summary>
    /// Provider-issued subaccount code (e.g. Paystack <c>ACCT_...</c>). Created
    /// when the member verifies their bank details. A disbursement is settled to
    /// the member through this subaccount after the Guarantor's payment is
    /// confirmed (ADR-044).
    /// </summary>
    public string? ProviderSubaccountCode { get; private set; }

    public PaymentRecipientStatus Status { get; private set; } = PaymentRecipientStatus.Unverified;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    protected PaymentRecipient()
    {
    }

    /// <summary>
    /// Creates a recipient pending provider verification.
    /// </summary>
    public static PaymentRecipient Create(
        Guid memberId,
        string provider,
        string bankCode,
        string bankName,
        string accountNumber,
        string accountName)
    {
        if (memberId == Guid.Empty)
        {
            throw new InvalidPaymentException("A member is required for a payment recipient.");
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new InvalidPaymentException("Payment provider is required.");
        }

        if (string.IsNullOrWhiteSpace(bankCode))
        {
            throw new InvalidPaymentException("Bank code is required.");
        }

        if (string.IsNullOrWhiteSpace(accountNumber) || accountNumber.Length < 6)
        {
            throw new InvalidPaymentException("A valid account number is required.");
        }

        if (string.IsNullOrWhiteSpace(accountName))
        {
            throw new InvalidPaymentException("Account name is required.");
        }

        return new PaymentRecipient
        {
            Id = Guid.NewGuid(),
            MemberId = memberId,
            Provider = provider.Trim(),
            BankCode = bankCode.Trim(),
            BankName = string.IsNullOrWhiteSpace(bankName) ? bankCode.Trim() : bankName.Trim(),
            AccountNumber = accountNumber.Trim(),
            AccountName = accountName.Trim(),
            Status = PaymentRecipientStatus.Unverified,
            IsActive = false,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Records the provider-issued recipient code after verification and marks
    /// the recipient active.
    /// </summary>
    public void VerifyAndActivate(string providerRecipientCode)
    {
        if (string.IsNullOrWhiteSpace(providerRecipientCode))
        {
            throw new InvalidPaymentException("Provider recipient code is required to activate a recipient.");
        }

        ProviderRecipientCode = providerRecipientCode.Trim();
        Status = PaymentRecipientStatus.Active;
        IsActive = true;
        Touch();
    }

    /// <summary>
    /// Records the provider-issued subaccount code used to settle disbursements
    /// to this member. A recipient has at most one active subaccount.
    /// </summary>
    public void AttachSubaccount(string providerSubaccountCode)
    {
        if (string.IsNullOrWhiteSpace(providerSubaccountCode))
        {
            throw new InvalidPaymentException("Provider subaccount code is required.");
        }

        ProviderSubaccountCode = providerSubaccountCode.Trim();
        Touch();
    }

    /// <summary>
    /// Marks the recipient as verified but not active.
    /// </summary>
    public void MarkVerified()
    {
        Status = PaymentRecipientStatus.Active;
        Touch();
    }

    /// <summary>
    /// Disables the recipient so it is no longer used for new disbursements.
    /// </summary>
    public void Disable()
    {
        Status = PaymentRecipientStatus.Disabled;
        IsActive = false;
        Touch();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
