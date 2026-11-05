using FamilyTrustFund.Domain.Loans;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Loans;

public class LoanTermRulesTests
{
    [Theory]
    [InlineData(6, RepaymentFrequency.Monthly, 6)]
    [InlineData(12, RepaymentFrequency.Monthly, 12)]
    [InlineData(6, RepaymentFrequency.Weekly, 26)]
    [InlineData(6, RepaymentFrequency.Biweekly, 13)]
    [InlineData(1, RepaymentFrequency.Weekly, 4)]
    [InlineData(1, RepaymentFrequency.Biweekly, 2)]
    public void ExpectedInstalments_uses_calendar_accurate_conversion(
        int termMonths,
        RepaymentFrequency frequency,
        int expected)
    {
        LoanTermRules.ExpectedInstalments(termMonths, frequency).Should().Be(expected);
    }

    [Fact]
    public void Validate_monthly_requires_exact_count()
    {
        LoanTermRules.Validate(6, 6, RepaymentFrequency.Monthly, 1, 60).Should().BeEmpty();

        LoanTermRules.Validate(6, 5, RepaymentFrequency.Monthly, 1, 60)
            .Should().ContainSingle(e => e.Contains("exactly 6"));
    }

    [Theory]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(27)]
    public void Validate_weekly_allows_one_instalment_either_side(int instalments)
    {
        LoanTermRules.Validate(6, instalments, RepaymentFrequency.Weekly, 1, 60).Should().BeEmpty();
    }

    [Fact]
    public void Validate_weekly_rejects_counts_outside_tolerance()
    {
        LoanTermRules.Validate(6, 24, RepaymentFrequency.Weekly, 1, 60)
            .Should().ContainSingle(e => e.Contains("about 26"));
    }

    [Fact]
    public void Validate_rejects_term_outside_fund_range()
    {
        var errors = LoanTermRules.Validate(2, 2, RepaymentFrequency.Monthly, 3, 12);

        errors.Should().ContainSingle(e => e.Contains("between 3 and 12 months"));
    }

    [Fact]
    public void Validate_rejects_zero_instalments()
    {
        LoanTermRules.Validate(6, 0, RepaymentFrequency.Monthly, 1, 60)
            .Should().ContainSingle(e => e.Contains("greater than zero"));
    }

    [Fact]
    public void Validate_rejects_extreme_term_without_overflowing()
    {
        LoanTermRules.Validate(int.MaxValue, 1, RepaymentFrequency.Weekly, 1, 60)
            .Should().ContainSingle(e => e.Contains("between 1 and 60"));
    }

    [Fact]
    public void ExpectedInstalments_rejects_extreme_term()
    {
        var act = () => LoanTermRules.ExpectedInstalments(int.MaxValue, RepaymentFrequency.Weekly);

        act.Should().Throw<InvalidLoanException>();
    }
}
