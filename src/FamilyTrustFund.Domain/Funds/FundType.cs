namespace FamilyTrustFund.Domain.Funds;

/// <summary>
/// The two supported fund types.
/// Family funds have no interest; External funds use a Guarantor-configured
/// interest rate.
/// </summary>
public enum FundType
{
    Family = 1,
    External = 2,
}
