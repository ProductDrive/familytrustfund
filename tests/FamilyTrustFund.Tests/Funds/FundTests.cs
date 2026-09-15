using FamilyTrustFund.Domain.Funds;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Funds;

public class FundTests
{
    private static readonly Guid Guarantor = Guid.NewGuid();

    [Fact]
    public void Create_family_fund_applies_default_multiplier_and_no_interest()
    {
        var fund = Fund.Create(Guarantor, "Aunties Fund", FundType.Family, 1_000_000m, "ABCD1234");

        fund.Type.Should().Be(FundType.Family);
        fund.CommittedCapital.Should().Be(1_000_000m);
        fund.ContributionMultiplier.Should().Be(Fund.DefaultContributionMultiplier);
        fund.InterestRate.Should().BeNull();
        fund.Status.Should().Be(FundStatus.Active);
        fund.JoinCode.Should().Be("ABCD1234");
        fund.GuarantorId.Should().Be(Guarantor);
    }

    [Fact]
    public void Create_external_fund_requires_interest_and_rejects_multiplier()
    {
        var fund = Fund.Create(Guarantor, "Biz Fund", FundType.External, 5_000_000m, "EFGH5678", interestRate: 15);

        fund.Type.Should().Be(FundType.External);
        fund.InterestRate.Should().Be(15m);
        fund.ContributionMultiplier.Should().Be(Fund.DefaultContributionMultiplier);
    }

    [Fact]
    public void Family_fund_with_interest_throws()
    {
        var act = () => Fund.Create(Guarantor, "F", FundType.Family, 100, "ABCD1234", interestRate: 10);
        act.Should().Throw<InvalidFundException>();
    }

    [Fact]
    public void External_fund_without_interest_throws()
    {
        var act = () => Fund.Create(Guarantor, "E", FundType.External, 100, "ABCD1234", interestRate: null);
        act.Should().Throw<InvalidFundException>();
    }

    [Fact]
    public void External_fund_with_multiplier_throws()
    {
        var act = () => Fund.Create(Guarantor, "E", FundType.External, 100, "ABCD1234", contributionMultiplier: 5, interestRate: 10);
        act.Should().Throw<InvalidFundException>();
    }

    [Fact]
    public void Negative_committed_capital_throws()
    {
        var act = () => Fund.Create(Guarantor, "F", FundType.Family, -1, "ABCD1234");
        act.Should().Throw<InvalidFundException>();
    }

    [Fact]
    public void Blank_name_throws()
    {
        var act = () => Fund.Create(Guarantor, "   ", FundType.Family, 100, "ABCD1234");
        act.Should().Throw<InvalidFundException>();
    }

    [Fact]
    public void Short_join_code_throws()
    {
        var act = () => Fund.Create(Guarantor, "F", FundType.Family, 100, "AB");
        act.Should().Throw<InvalidFundException>();
    }

    [Fact]
    public void Non_active_status_change_throws()
    {
        var fund = Fund.Create(Guarantor, "F", FundType.Family, 100, "ABCD1234");
        var act = () => fund.SetStatus(FundStatus.Transitioned);
        act.Should().Throw<InvalidFundException>();
    }

    [Fact]
    public void Family_transition_is_one_way()
    {
        var fund = Fund.Create(Guarantor, "F", FundType.Family, 100, "ABCD1234");
        fund.MarkTransitioned();
        fund.Status.Should().Be(FundStatus.Transitioned);
        fund.TransitionedAtUtc.Should().NotBeNull();
        var second = () => fund.MarkTransitioned();
        second.Should().Throw<InvalidFundException>();
    }

    [Fact]
    public void External_fund_cannot_transition()
    {
        var fund = Fund.Create(Guarantor, "E", FundType.External, 100, "ABCD1234", interestRate: 10);
        var act = () => fund.MarkTransitioned();
        act.Should().Throw<InvalidFundException>();
    }

    [Fact]
    public void How_it_works_is_settable_and_trimmable()
    {
        var fund = Fund.Create(Guarantor, "F", FundType.Family, 100, "ABCD1234");
        fund.SetHowItWorks("  Everyone contributes monthly.  ");

        fund.HowItWorks.Should().Be("Everyone contributes monthly.");
    }

    [Fact]
    public void How_it_works_accepts_clear_and_blank()
    {
        var fund = Fund.Create(Guarantor, "F", FundType.Family, 100, "ABCD1234");
        fund.SetHowItWorks(" Some guidance ");
        fund.SetHowItWorks(null);
        fund.HowItWorks.Should().BeNull();
    }

    [Fact]
    public void Overlong_how_it_works_throws()
    {
        var fund = Fund.Create(Guarantor, "F", FundType.Family, 100, "ABCD1234");
        var act = () => fund.SetHowItWorks(new string('x', 4001));
        act.Should().Throw<InvalidFundException>();
    }

    [Fact]
    public void Join_code_generator_produces_valid_codes()
    {
        var code = JoinCodeGenerator.Generate();
        code.Should().HaveLength(8);
        code.ToCharArray().Should().OnlyContain(c => "ABCDEFGHJKMNPQRSTUVWXYZ23456789".Contains(c));
    }

    [Fact]
    public void Join_code_generator_returns_distinct_codes()
    {
        var codes = Enumerable.Range(0, 100).Select(_ => JoinCodeGenerator.Generate()).ToHashSet();
        codes.Count.Should().Be(100);
    }
}
