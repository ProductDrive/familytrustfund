using FamilyTrustFund.Domain.Funds;

namespace FamilyTrustFund.Domain.Financial;

/// <summary>
/// Server-authoritative financial calculations.
/// </summary>
/// <remarks>
/// These rules must never be re-implemented by clients. Balances, capacities,
/// entitlements and repayment amounts are always computed here or derived from
/// authoritative ledger records.
/// </remarks>
public static class FundFinancialRules
{
    /// <summary>
    /// Available Lending Capacity = committed capital - active disbursed loans.
    /// </summary>
    /// <remarks>
    /// Only loans that have actually been <i>disbursed</i> count as active
    /// disbursed loans. Approved-but-not-disbursed loans do not reduce capacity.
    /// </remarks>
    public static decimal AvailableLendingCapacity(Fund fund, decimal activeDisbursedLoans)
    {
        if (activeDisbursedLoans < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activeDisbursedLoans),
                "Active disbursed loans cannot be negative.");
        }

        return fund.CommittedCapital - activeDisbursedLoans;
    }

    /// <summary>
    /// A Family member's borrowing entitlement is their Fund Credit multiplied
    /// by the configured contribution multiplier.
    /// </summary>
    /// <remarks>
    /// Entitlement is a ceiling on the <i>request</i>. The permitted loan also
    /// respects the fund's currently available lending capacity.
    /// </remarks>
    public static decimal FamilyBorrowingEntitlement(decimal fundCredit, decimal contributionMultiplier)
    {
        if (fundCredit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fundCredit), "Fund Credit cannot be negative.");
        }

        if (contributionMultiplier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(contributionMultiplier), "Multiplier must be at least 1.");
        }

        return fundCredit * contributionMultiplier;
    }
}
