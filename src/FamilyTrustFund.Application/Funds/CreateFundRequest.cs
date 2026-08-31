using FamilyTrustFund.Domain.Funds;

namespace FamilyTrustFund.Application.Funds;

/// <summary>
/// Data required to create a new lending fund.
/// </summary>
public sealed class CreateFundRequest
{
    public string Name { get; init; } = string.Empty;
    public FundType Type { get; init; }
    public decimal CommittedCapital { get; init; }
    public decimal? ContributionMultiplier { get; init; }
    public decimal? InterestRate { get; init; }
    public string? JoinCode { get; init; }
}
