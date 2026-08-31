using FamilyTrustFund.Domain.Financial;
using FamilyTrustFund.Domain.Funds;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Financial;

public class FundFinancialRulesTests
{
    private static Fund FamilyFund(decimal committed) =>
        Fund.Create(Guid.NewGuid(), "F", FundType.Family, committed, "ABCD1234");

    [Fact]
    public void Capacity_equals_committed_minus_active_disbursed()
    {
        var fund = FamilyFund(1_000_000m);
        FundFinancialRules.AvailableLendingCapacity(fund, 300_000m).Should().Be(700_000m);
    }

    [Fact]
    public void Approved_but_not_disbursed_loan_does_not_reduce_capacity()
    {
        // ADR-003: only disbursed loans reduce capacity. Passing zero active
        // disbursed loans (approved-but-not-disbursed) leaves capacity intact.
        var fund = FamilyFund(1_000_000m);
        FundFinancialRules.AvailableLendingCapacity(fund, 0m).Should().Be(1_000_000m);
    }

    [Fact]
    public void Capacity_cannot_be_negative_for_negative_disbursed()
    {
        var fund = FamilyFund(100m);
        var act = () => FundFinancialRules.AvailableLendingCapacity(fund, -1m);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Capacity_can_be_zero_when_fully_committed()
    {
        var fund = FamilyFund(500_000m);
        FundFinancialRules.AvailableLendingCapacity(fund, 500_000m).Should().Be(0m);
    }

    [Fact]
    public void Family_entitlement_is_fund_credit_times_multiplier()
    {
        FundFinancialRules.FamilyBorrowingEntitlement(50_000m, 10m).Should().Be(500_000m);
    }

    [Fact]
    public void Family_entitlement_uses_configured_multiplier()
    {
        FundFinancialRules.FamilyBorrowingEntitlement(50_000m, 5m).Should().Be(250_000m);
    }

    [Fact]
    public void Negative_fund_credit_throws()
    {
        var act = () => FundFinancialRules.FamilyBorrowingEntitlement(-1m, 10m);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Multiplier_below_one_throws()
    {
        var act = () => FundFinancialRules.FamilyBorrowingEntitlement(100m, 0.5m);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
