namespace FamilyTrustFund.Domain.Funds;

/// <summary>
/// A lending fund profile owned by a Guarantor.
/// </summary>
/// <remarks>
/// Committed capital is an indication of money the Guarantor is willing to
/// spend; it is <b>not</b> money deposited into the platform. Separate Family
/// contributions and committed lending capital are tracked as distinct pools.
/// </remarks>
public class Fund
{
    public const decimal DefaultContributionMultiplier = 10m;
    public const int DefaultMinLoanTermMonths = 1;
    public const int DefaultMaxLoanTermMonths = 60;
    public const int AbsoluteMaxLoanTermMonths = 120;

    public Guid Id { get; private set; }

    /// <summary>
    /// The Fund Originator. For a single-Guarantor fund this is the sole owner;
    /// for a Group Guarantor fund this is the Guarantor who created the fund and
    /// is the only one who may initiate disbursement (ADR-051, ADR-053).
    /// </summary>
    public Guid GuarantorId { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public FundType Type { get; private set; }
    public string JoinCode { get; private set; } = string.Empty;

    /// <summary>
    /// The authoritative committed-capital figure used by the capacity
    /// calculation (ADR-002, ADR-003). For a Group Guarantor fund this is the
    /// aggregate of the active Guarantors' individual commitments.
    /// </summary>
    public decimal CommittedCapital { get; private set; }

    /// <summary>
    /// Single-Guarantor or Group Guarantor operation. Immutable after creation.
    /// Existing funds are single-Guarantor (ADR-051).
    /// </summary>
    public FundGuarantorMode GuarantorMode { get; private set; } = FundGuarantorMode.Single;

    public bool IsGroupFund => GuarantorMode == FundGuarantorMode.Group;

    /// <summary>Family borrowing entitlement multiplier (Family funds only).</summary>
    public decimal ContributionMultiplier { get; private set; }

    /// <summary>Annual interest rate as a percentage (External funds only).</summary>
    public decimal? InterestRate { get; private set; }

    /// <summary>Minimum loan term in months allowed by this fund.</summary>
    public int MinLoanTermMonths { get; private set; } = DefaultMinLoanTermMonths;

    /// <summary>Maximum loan term in months allowed by this fund.</summary>
    public int MaxLoanTermMonths { get; private set; } = DefaultMaxLoanTermMonths;

    /// <summary>Guarantor-authored guidance shown to members of this fund.</summary>
    public string? HowItWorks { get; private set; }

    /// <summary>Bank name for loan repayments.</summary>
    public string? RepaymentBankName { get; private set; }

    /// <summary>Account number for loan repayments.</summary>
    public string? RepaymentAccountNumber { get; private set; }

    /// <summary>Account name for loan repayments.</summary>
    public string? RepaymentAccountName { get; private set; }

    public FundStatus Status { get; private set; } = FundStatus.Active;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// When this Family fund transitioned to Family Capital. Null until the
    /// one-way transition (ADR-008) is confirmed by the Guarantor.
    /// </summary>
    public DateTime? TransitionedAtUtc { get; private set; }

    protected Fund() { }

    public static Fund Create(
        Guid guarantorId,
        string name,
        FundType type,
        decimal committedCapital,
        string joinCode,
        decimal? contributionMultiplier = null,
        decimal? interestRate = null,
        int? minLoanTermMonths = null,
        int? maxLoanTermMonths = null,
        FundGuarantorMode guarantorMode = FundGuarantorMode.Single)
    {
        if (guarantorId == Guid.Empty)
        {
            throw new InvalidFundException("A Guarantor is required to create a fund.");
        }

        var fund = new Fund
        {
            Id = Guid.NewGuid(),
            GuarantorId = guarantorId,
            Type = type,
            Status = FundStatus.Active,
            GuarantorMode = guarantorMode,
        };
        fund.Rename(name);
        fund.SetJoinCode(joinCode);
        fund.SetCommittedCapital(committedCapital);
        fund.SetTypeConfiguration(type, contributionMultiplier, interestRate);
        fund.SetLoanTermRange(
            minLoanTermMonths ?? DefaultMinLoanTermMonths,
            maxLoanTermMonths ?? DefaultMaxLoanTermMonths);
        return fund;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidFundException("Fund name is required.");
        }

        Name = name.Trim();
        Touch();
    }

    public void SetJoinCode(string joinCode)
    {
        if (string.IsNullOrWhiteSpace(joinCode) || joinCode.Length < 4 || joinCode.Length > 64)
        {
            throw new InvalidFundException("A join code of 4 to 64 characters is required.");
        }

        JoinCode = joinCode.Trim().ToUpperInvariant();
        Touch();
    }

    public void SetCommittedCapital(decimal committedCapital)
    {
        if (committedCapital < 0)
        {
            throw new InvalidFundException("Committed capital cannot be negative.");
        }

        CommittedCapital = committedCapital;
        Touch();
    }

    /// <summary>
    /// Raises the fund's aggregate committed capital. Used when a Group
    /// Guarantor joins or increases their own commitment (ADR-051).
    /// </summary>
    public void IncreaseCommittedCapital(decimal amount)
    {
        if (amount <= 0)
        {
            throw new InvalidFundException("An increase in committed capital must be greater than zero.");
        }

        CommittedCapital += amount;
        Touch();
    }

    /// <summary>
    /// Lowers the fund's aggregate committed capital, refusing any change that
    /// would drop committed capital below the capital already disbursed and
    /// still active. Committed capital is a planning figure (ADR-002), so it may
    /// never shrink below money that is actually out.
    /// </summary>
    public void DecreaseCommittedCapital(decimal amount, decimal activeDisbursedLoans)
    {
        if (amount <= 0)
        {
            throw new InvalidFundException("A decrease in committed capital must be greater than zero.");
        }

        var remaining = CommittedCapital - amount;
        if (remaining < 0)
        {
            throw new InvalidFundException("Committed capital cannot become negative.");
        }

        if (remaining < activeDisbursedLoans)
        {
            throw new InvalidFundException(
                "Committed capital cannot be reduced below the capital already disbursed to active loans.");
        }

        CommittedCapital = remaining;
        Touch();
    }

    /// <summary>
    /// Sets the loan term range allowed for this fund. The Guarantor configures
    /// this at fund creation; the range bounds every member request and every
    /// Guarantor approval.
    /// </summary>
    public void SetLoanTermRange(int minTermMonths, int maxTermMonths)
    {
        if (minTermMonths < 1)
        {
            throw new InvalidFundException("Minimum loan term must be at least 1 month.");
        }

        if (maxTermMonths < minTermMonths)
        {
            throw new InvalidFundException("Maximum loan term cannot be less than the minimum loan term.");
        }

        if (maxTermMonths > AbsoluteMaxLoanTermMonths)
        {
            throw new InvalidFundException($"Maximum loan term cannot exceed {AbsoluteMaxLoanTermMonths} months.");
        }

        MinLoanTermMonths = minTermMonths;
        MaxLoanTermMonths = maxTermMonths;
        Touch();
    }

    public void SetHowItWorks(string? content)
    {
        if (content is not null && content.Length > 4000)
        {
            throw new InvalidFundException("How It Works content cannot exceed 4000 characters.");
        }

        HowItWorks = string.IsNullOrWhiteSpace(content) ? null : content.Trim();
        Touch();
    }

    public void SetRepaymentAccount(string? bankName, string? accountNumber, string? accountName)
    {
        if (string.IsNullOrWhiteSpace(bankName) && string.IsNullOrWhiteSpace(accountNumber) && string.IsNullOrWhiteSpace(accountName))
        {
            RepaymentBankName = null;
            RepaymentAccountNumber = null;
            RepaymentAccountName = null;
            Touch();
            return;
        }

        if (string.IsNullOrWhiteSpace(bankName) || string.IsNullOrWhiteSpace(accountNumber) || string.IsNullOrWhiteSpace(accountName))
        {
            throw new InvalidFundException("All repayment account fields are required when setting repayment details.");
        }

        RepaymentBankName = bankName.Trim();
        RepaymentAccountNumber = accountNumber.Trim();
        RepaymentAccountName = accountName.Trim();
        Touch();
    }

    public void SetTypeConfiguration(FundType type, decimal? contributionMultiplier, decimal? interestRate)
    {
        Type = type;

        switch (type)
        {
            case FundType.Family:
                if (interestRate is not null)
                {
                    throw new InvalidFundException("Family funds do not charge interest.");
                }

                ContributionMultiplier = contributionMultiplier ?? DefaultContributionMultiplier;
                if (ContributionMultiplier < 1)
                {
                    throw new InvalidFundException("Contribution multiplier must be at least 1.");
                }

                InterestRate = null;
                break;

            case FundType.External:
                if (contributionMultiplier is not null)
                {
                    throw new InvalidFundException("Contribution multiplier applies only to Family funds.");
                }

                if (interestRate is null or < 0)
                {
                    throw new InvalidFundException("An External fund requires a non-negative interest rate.");
                }

                InterestRate = interestRate;
                ContributionMultiplier = DefaultContributionMultiplier;
                break;

            default:
                throw new InvalidFundException("Unknown fund type.");
        }

        Touch();
    }

    public void SetStatus(FundStatus status)
    {
        if (status != FundStatus.Active && status != FundStatus.Inactive)
        {
            throw new InvalidFundException("Status can only be set to Active or Inactive here. Transition is a dedicated, one-way operation.");
        }

        Status = status;
        Touch();
    }

    /// <summary>
    /// Marks the fund as transitioned to Family Capital. One-way and permanent.
    /// Only valid for Family funds.
    /// </summary>
    public void MarkTransitioned()
    {
        if (Type != FundType.Family)
        {
            throw new InvalidFundException("Only Family funds can transition to Family Capital.");
        }

        if (Status == FundStatus.Transitioned)
        {
            throw new InvalidFundException("Fund has already transitioned to Family Capital.");
        }

        Status = FundStatus.Transitioned;
        TransitionedAtUtc = DateTime.UtcNow;
        Touch();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
