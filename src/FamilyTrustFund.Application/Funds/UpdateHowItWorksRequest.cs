namespace FamilyTrustFund.Application.Funds;

/// <summary>
/// Content a Guarantor sets to tell members how the fund works.
/// </summary>
public sealed class UpdateHowItWorksRequest
{
    public string? Content { get; init; }
}