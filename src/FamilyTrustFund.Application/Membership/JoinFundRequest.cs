namespace FamilyTrustFund.Application.Membership;

/// <summary>
/// Data supplied by a member when joining a fund with the Guarantor's code.
/// </summary>
public sealed class JoinFundRequest
{
    public string JoinCode { get; init; } = string.Empty;
}