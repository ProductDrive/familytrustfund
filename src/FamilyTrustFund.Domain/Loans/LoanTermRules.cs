namespace FamilyTrustFund.Domain.Loans;

/// <summary>
/// Server-authoritative rules for loan terms. A loan is expressed to members as
/// a term in months plus a number of instalments; the two must be consistent
/// with the selected repayment frequency. Clients never calculate these values
/// (AGENTS §7).
/// </summary>
public static class LoanTermRules
{
    public const int DefaultMinTermMonths = 1;
    public const int DefaultMaxTermMonths = 60;

    /// <summary>Absolute ceiling for a fund's configured maximum term.</summary>
    public const int AbsoluteMaxTermMonths = 120;

    /// <summary>
    /// The number of instalments implied by a term in months and a repayment
    /// frequency. Weekly and biweekly terms use calendar-accurate month-to-week
    /// conversion (52/12 and 26/12) rather than a flat 4 or 2 per month.
    /// </summary>
    public static int ExpectedInstalments(int termMonths, RepaymentFrequency frequency)
    {
        if (termMonths <= 0)
        {
            throw new InvalidLoanException("Loan term must be greater than zero months.");
        }

        if (termMonths > AbsoluteMaxTermMonths)
        {
            throw new InvalidLoanException(
                $"Loan term cannot exceed {AbsoluteMaxTermMonths} months.");
        }

        var count = frequency switch
        {
            RepaymentFrequency.Weekly => (int)Math.Round(termMonths * 52m / 12m, MidpointRounding.AwayFromZero),
            RepaymentFrequency.Biweekly => (int)Math.Round(termMonths * 26m / 12m, MidpointRounding.AwayFromZero),
            RepaymentFrequency.Monthly => termMonths,
            _ => throw new InvalidLoanException("Unknown repayment frequency."),
        };

        return Math.Max(1, count);
    }

    /// <summary>
    /// Calendar rounding tolerance. Monthly terms are exact; weekly and
    /// biweekly terms allow one instalment either side of the expected count.
    /// </summary>
    public static int Tolerance(RepaymentFrequency frequency) =>
        frequency == RepaymentFrequency.Monthly ? 0 : 1;

    /// <summary>
    /// Validates a term/instalment combination against a fund's configured
    /// range. Returns human-readable errors; an empty list means valid.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        int termMonths,
        int instalments,
        RepaymentFrequency frequency,
        int minTermMonths,
        int maxTermMonths)
    {
        var errors = new List<string>();

        if (termMonths < minTermMonths || termMonths > maxTermMonths)
        {
            // The count cannot be meaningful when the term itself is outside the
            // fund's range, and computing an expected count for an extreme term
            // would overflow. Report the range error and stop.
            errors.Add($"Loan term must be between {minTermMonths} and {maxTermMonths} months.");
            return errors;
        }

        if (instalments <= 0)
        {
            errors.Add("Number of instalments must be greater than zero.");
            return errors;
        }

        if (termMonths <= 0)
        {
            return errors;
        }

        var expected = ExpectedInstalments(termMonths, frequency);
        var tolerance = Tolerance(frequency);

        if (Math.Abs(instalments - expected) > tolerance)
        {
            var minimum = Math.Max(1, expected - tolerance);
            var maximum = expected + tolerance;

            errors.Add(tolerance == 0
                ? $"Monthly repayments over {termMonths} month(s) require exactly {expected} instalments."
                : $"{frequency} repayments over {termMonths} month(s) require about {expected} instalments (allowed {minimum}–{maximum}).");
        }

        return errors;
    }
}
