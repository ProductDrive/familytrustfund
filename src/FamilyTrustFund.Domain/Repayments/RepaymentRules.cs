using FamilyTrustFund.Domain.Loans;

namespace FamilyTrustFund.Domain.Repayments;

/// <summary>
/// Server-authoritative repayment calculations. These must never be
/// re-implemented by clients (AGENTS §7).
/// </summary>
public static class RepaymentRules
{
    /// <summary>
    /// Splits interest into a per-instalment share for the MVP. A more
    /// sophisticated amortisation model can be introduced later without
    /// changing the domain contract (mirrors LoanService.CalculateTotalRepayable).
    /// </summary>
    public static IReadOnlyList<Instalment> BuildV1Instalments(
        Guid scheduleId,
        decimal approvedAmount,
        decimal totalRepayable,
        RepaymentFrequency frequency,
        int term,
        DateTime startDateUtc)
    {
        if (approvedAmount <= 0)
        {
            throw new InvalidRepaymentException("Approved amount must be greater than zero.");
        }

        if (totalRepayable < approvedAmount)
        {
            throw new InvalidRepaymentException("Total repayable must be at least the approved amount.");
        }

        if (term <= 0)
        {
            throw new InvalidRepaymentException("Repayment term must be a positive number of instalments.");
        }

        var totalInterest = totalRepayable - approvedAmount;
        var principalPerItem = Math.Round(approvedAmount / term, 2, MidpointRounding.AwayFromZero);
        var interestPerItem = Math.Round(totalInterest / term, 2, MidpointRounding.AwayFromZero);

        var result = new List<Instalment>(term);
        var expectedTotal = 0m;
        var principalTotal = 0m;
        var interestTotal = 0m;

        for (var i = 0; i < term; i++)
        {
            var isLast = i == term - 1;

            // The last instalment absorbs rounding so the schedule exactly
            // matches the approved amount and total repayable.
            var principal = isLast ? approvedAmount - principalTotal : principalPerItem;
            var interest = isLast ? totalInterest - interestTotal : interestPerItem;

            expectedTotal += principal + interest;
            principalTotal += principal;
            interestTotal += interest;

            result.Add(new Instalment
            {
                Sequence = i + 1,
                DueDateUtc = AddFrequency(startDateUtc, frequency, i + 1),
                PrincipalDue = principal,
                InterestDue = interest,
                ExpectedAmount = principal + interest,
            });
        }

        return result;
    }

    /// <summary>
    /// Spreads the remaining outstanding capital across the remaining
    /// instalments. Used when a confirmed lump-sum recalculates the remaining
    /// schedule; interest on already-allocated items is preserved separately.
    /// </summary>
    public static IReadOnlyList<Instalment> BuildAmortisedInstalments(
        Guid scheduleId,
        decimal remainingPrincipal,
        int remainingTerm,
        RepaymentFrequency frequency,
        DateTime startDateUtc)
    {
        if (remainingPrincipal < 0)
        {
            throw new InvalidRepaymentException("Remaining principal cannot be negative.");
        }

        if (remainingTerm <= 0)
        {
            throw new InvalidRepaymentException("Remaining term must be positive.");
        }

        var principalPerItem = Math.Round(remainingPrincipal / remainingTerm, 2, MidpointRounding.AwayFromZero);
        var result = new List<Instalment>(remainingTerm);
        var principalTotal = 0m;

        for (var i = 0; i < remainingTerm; i++)
        {
            var isLast = i == remainingTerm - 1;
            var principal = isLast ? remainingPrincipal - principalTotal : principalPerItem;
            principalTotal += principal;

            result.Add(new Instalment
            {
                Sequence = i + 1,
                DueDateUtc = AddFrequency(startDateUtc, frequency, i + 1),
                PrincipalDue = Math.Round(principal, 2, MidpointRounding.AwayFromZero),
                InterestDue = 0m,
                ExpectedAmount = Math.Round(principal, 2, MidpointRounding.AwayFromZero),
            });
        }

        return result;
    }

    /// <summary>
    /// Adds one frequency interval a given number of times to a base date.
    /// </summary>
    public static DateTime AddFrequency(DateTime baseDate, RepaymentFrequency frequency, int intervals)
    {
        return frequency switch
        {
            RepaymentFrequency.Weekly => baseDate.AddDays(7 * intervals),
            RepaymentFrequency.Biweekly => baseDate.AddDays(14 * intervals),
            RepaymentFrequency.Monthly => baseDate.AddMonths(intervals),
            _ => throw new InvalidRepaymentException("Unknown repayment frequency."),
        };
    }

    /// <summary>Repayment surplus = amount received - amount expected (AGENTS §2.7).</summary>
    public static decimal Surplus(decimal amountReceived, decimal amountExpected)
    {
        if (amountReceived < 0)
        {
            throw new InvalidRepaymentException("Amount received cannot be negative.");
        }

        return Math.Round(amountReceived - amountExpected, 2, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// A computed instalment before it is persisted.
/// </summary>
public sealed class Instalment
{
    public int Sequence { get; init; }
    public DateTime DueDateUtc { get; init; }
    public decimal PrincipalDue { get; init; }
    public decimal InterestDue { get; init; }
    public decimal ExpectedAmount { get; init; }
}
