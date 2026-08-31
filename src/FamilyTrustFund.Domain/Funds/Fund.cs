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

    public Guid Id { get; private set; }
    public Guid GuarantorId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public FundType Type { get; private set; }
    public string JoinCode { get; private set; } = string.Empty;
    public decimal CommittedCapital { get; private set; }

    /// <summary>Family borrowing entitlement multiplier (Family funds only).</summary>
    public decimal ContributionMultiplier { get; private set; }

    /// <summary>Annual interest rate as a percentage (External funds only).</summary>
    public decimal? InterestRate { get; private set; }

    /// <summary>Guarantor-authored guidance shown to members of this fund.</summary>
    public string? HowItWorks { get; private set; }

    public FundStatus Status { get; private set; } = FundStatus.Active;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    protected Fund() { }

    public static Fund Create(
        Guid guarantorId,
        string name,
        FundType type,
        decimal committedCapital,
        string joinCode,
        decimal? contributionMultiplier = null,
        decimal? interestRate = null)
    {
        var fund = new Fund
        {
            Id = Guid.NewGuid(),
            GuarantorId = guarantorId,
            Type = type,
            Status = FundStatus.Active,
        };
        fund.Rename(name);
        fund.SetJoinCode(joinCode);
        fund.SetCommittedCapital(committedCapital);
        fund.SetTypeConfiguration(type, contributionMultiplier, interestRate);
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

    public void SetHowItWorks(string? content)
    {
        if (content is not null && content.Length > 4000)
        {
            throw new InvalidFundException("How It Works content cannot exceed 4000 characters.");
        }

        HowItWorks = string.IsNullOrWhiteSpace(content) ? null : content.Trim();
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
        Touch();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
