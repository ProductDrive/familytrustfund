using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Tests.Support;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Funds;

public class FundTransitionServiceTests
{
    private static (FundTransitionService Service, FakeFundRepository Funds, FakeContributionRepository Contributions, FakeAuditLog Audit)
        Create(Guid guarantor)
    {
        var funds = new FakeFundRepository();
        var contributions = new FakeContributionRepository();
        var audit = new FakeAuditLog();
        var service = new FundTransitionService(funds, contributions, audit);
        return (service, funds, contributions, audit);
    }

    [Fact]
    public async Task Status_is_scoped_to_fund_ownership()
    {
        var owner = Guid.NewGuid();
        var (service, funds, _, _) = Create(owner);
        var fund = Fund.Create(owner, "Aunties Fund", FundType.Family, 1_000_000m, "ABCD1234");
        funds.Add(fund);

        var status = await service.GetTransitionStatusAsync(owner, fund.Id);
        status.Should().NotBeNull();
        status!.FundId.Should().Be(fund.Id);

        var other = await service.GetTransitionStatusAsync(Guid.NewGuid(), fund.Id);
        other.Should().BeNull();
    }

    [Fact]
    public async Task Status_reports_contributions_and_committed_capital_as_separate_pools_below_threshold()
    {
        var guarantor = Guid.NewGuid();
        var (service, funds, contributions, _) = Create(guarantor);
        var fund = Fund.Create(guarantor, "F", FundType.Family, 1_000_000m, "ABCD1234");
        funds.Add(fund);
        contributions.ConfirmedByFund[fund.Id] = 400_000m;

        var status = await service.GetTransitionStatusAsync(guarantor, fund.Id);

        status!.CommittedCapital.Should().Be(1_000_000m);
        status.TotalFamilyContributions.Should().Be(400_000m);
        status.RemainingToThreshold.Should().Be(600_000m);
        status.IsFamilyFund.Should().BeTrue();
        status.IsTransitioned.Should().BeFalse();
        status.IsEligible.Should().BeFalse();
    }

    [Fact]
    public async Task Status_is_eligible_when_contributions_reach_committed_capital()
    {
        var guarantor = Guid.NewGuid();
        var (service, funds, contributions, _) = Create(guarantor);
        var fund = Fund.Create(guarantor, "F", FundType.Family, 1_000_000m, "ABCD1234");
        funds.Add(fund);
        contributions.ConfirmedByFund[fund.Id] = 1_000_000m;

        var status = await service.GetTransitionStatusAsync(guarantor, fund.Id);

        status!.IsEligible.Should().BeTrue();
        status.RemainingToThreshold.Should().Be(0m);
    }

    [Fact]
    public async Task Status_is_never_eligible_for_external_funds()
    {
        var guarantor = Guid.NewGuid();
        var (service, funds, _, _) = Create(guarantor);
        var fund = Fund.Create(guarantor, "E", FundType.External, 1_000_000m, "ABCD1234", interestRate: 10);
        funds.Add(fund);

        var status = await service.GetTransitionStatusAsync(guarantor, fund.Id);

        status!.IsFamilyFund.Should().BeFalse();
        status.IsEligible.Should().BeFalse();
        status.TotalFamilyContributions.Should().Be(0m);
    }

    [Fact]
    public async Task Status_reports_already_transitioned()
    {
        var guarantor = Guid.NewGuid();
        var (service, funds, contributions, _) = Create(guarantor);
        var fund = Fund.Create(guarantor, "F", FundType.Family, 1_000_000m, "ABCD1234");
        funds.Add(fund);
        contributions.ConfirmedByFund[fund.Id] = 1_000_000m;

        var result = await service.TransitionToFamilyCapitalAsync(guarantor, fund.Id);
        result.Should().NotBeNull();

        var status = await service.GetTransitionStatusAsync(guarantor, fund.Id);
        status!.IsTransitioned.Should().BeTrue();
        status.IsEligible.Should().BeFalse();
    }

    [Fact]
    public async Task Transition_is_manual_and_records_audit()
    {
        var guarantor = Guid.NewGuid();
        var (service, funds, contributions, audit) = Create(guarantor);
        var fund = Fund.Create(guarantor, "F", FundType.Family, 1_000_000m, "ABCD1234");
        funds.Add(fund);
        contributions.ConfirmedByFund[fund.Id] = 1_200_000m;

        var result = await service.TransitionToFamilyCapitalAsync(guarantor, fund.Id);

        result.Should().NotBeNull();
        result!.CommittedCapital.Should().Be(1_000_000m);
        result.TotalFamilyContributions.Should().Be(1_200_000m);
        result.TransitionedAtUtc.Should().NotBe(default);

        fund.Status.Should().Be(FundStatus.Transitioned);
        fund.TransitionedAtUtc.Should().NotBeNull();
        audit.Events.Should().ContainSingle(e =>
            e.Action == "Fund.TransitionedToFamilyCapital" && e.ResourceId == fund.Id);
    }

    [Fact]
    public async Task Transition_rejects_when_contributions_below_committed_capital()
    {
        var guarantor = Guid.NewGuid();
        var (service, funds, contributions, audit) = Create(guarantor);
        var fund = Fund.Create(guarantor, "F", FundType.Family, 1_000_000m, "ABCD1234");
        funds.Add(fund);
        contributions.ConfirmedByFund[fund.Id] = 999_999m;

        var act = async () => await service.TransitionToFamilyCapitalAsync(guarantor, fund.Id);

        await act.Should().ThrowAsync<InvalidFundException>();
        fund.Status.Should().Be(FundStatus.Active);
        audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Transition_rejects_fund_not_owned_by_guarantor()
    {
        var owner = Guid.NewGuid();
        var (service, funds, _, _) = Create(owner);
        var fund = Fund.Create(owner, "F", FundType.Family, 100m, "ABCD1234");
        funds.Add(fund);

        var result = await service.TransitionToFamilyCapitalAsync(Guid.NewGuid(), fund.Id);

        result.Should().BeNull();
        fund.Status.Should().Be(FundStatus.Active);
    }

    [Fact]
    public async Task Transition_rejects_external_fund()
    {
        var guarantor = Guid.NewGuid();
        var (service, funds, _, _) = Create(guarantor);
        var fund = Fund.Create(guarantor, "E", FundType.External, 100m, "ABCD1234", interestRate: 10);
        funds.Add(fund);

        var act = async () => await service.TransitionToFamilyCapitalAsync(guarantor, fund.Id);

        await act.Should().ThrowAsync<InvalidFundException>();
        fund.Status.Should().Be(FundStatus.Active);
    }

    [Fact]
    public async Task Transition_is_one_way_in_the_service()
    {
        var guarantor = Guid.NewGuid();
        var (service, funds, contributions, _) = Create(guarantor);
        var fund = Fund.Create(guarantor, "F", FundType.Family, 100m, "ABCD1234");
        funds.Add(fund);
        contributions.ConfirmedByFund[fund.Id] = 100m;

        (await service.TransitionToFamilyCapitalAsync(guarantor, fund.Id)).Should().NotBeNull();
        var again = async () => await service.TransitionToFamilyCapitalAsync(guarantor, fund.Id);

        await again.Should().ThrowAsync<InvalidFundException>();
        fund.Status.Should().Be(FundStatus.Transitioned);
    }

    [Fact]
    public async Task Transition_rejects_inactive_fund()
    {
        var guarantor = Guid.NewGuid();
        var (service, funds, _, _) = Create(guarantor);
        var fund = Fund.Create(guarantor, "F", FundType.Family, 100m, "ABCD1234");
        funds.Add(fund);
        fund.SetStatus(FundStatus.Inactive);

        var act = async () => await service.TransitionToFamilyCapitalAsync(guarantor, fund.Id);

        await act.Should().ThrowAsync<InvalidFundException>();
        fund.Status.Should().Be(FundStatus.Inactive);
    }
}