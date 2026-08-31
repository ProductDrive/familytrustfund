using FamilyTrustFund.Domain.Funds;

namespace FamilyTrustFund.Application.Funds;

/// <summary>
/// Server-calculated representation of a fund returned to clients.
/// </summary>
public sealed class FundDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public FundType Type { get; init; }
    public string JoinCode { get; init; } = string.Empty;
    public decimal CommittedCapital { get; init; }
    public decimal ContributionMultiplier { get; init; }
    public decimal? InterestRate { get; init; }
    public string? HowItWorks { get; init; }
    public FundStatus Status { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
