using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Tests.Support;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Funds;

public class FundServiceTests
{
    private static FundService CreateService(FakeFundRepository repo) =>
        new(repo, new FakeAuditLog());

    [Fact]
    public async Task Create_generates_code_when_none_supplied()
    {
        var service = CreateService(new FakeFundRepository());
        var fund = await service.CreateFundAsync(Guid.NewGuid(), new CreateFundRequest
        {
            Name = "Aunties Fund",
            Type = FundType.Family,
            CommittedCapital = 1_000_000m,
        });

        fund.JoinCode.Should().NotBeNullOrWhiteSpace();
        fund.GuarantorId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Create_uses_supplied_code()
    {
        var service = CreateService(new FakeFundRepository());
        var fund = await service.CreateFundAsync(Guid.NewGuid(), new CreateFundRequest
        {
            Name = "Biz Fund",
            Type = FundType.External,
            CommittedCapital = 500_000m,
            InterestRate = 12,
            JoinCode = "MYCODE12",
        });

        fund.JoinCode.Should().Be("MYCODE12");
    }

    [Fact]
    public async Task Create_rejects_duplicate_join_code_for_same_guarantor()
    {
        var service = CreateService(new FakeFundRepository());
        var guarantor = Guid.NewGuid();
        await service.CreateFundAsync(guarantor, new CreateFundRequest
        {
            Name = "First",
            Type = FundType.Family,
            CommittedCapital = 100,
            JoinCode = "DUPCODE1",
        });

        var act = async () => await service.CreateFundAsync(guarantor, new CreateFundRequest
        {
            Name = "Second",
            Type = FundType.Family,
            CommittedCapital = 100,
            JoinCode = "DUPCODE1",
        });

        await act.Should().ThrowAsync<InvalidFundException>();
    }

    [Fact]
    public async Task Same_join_code_allowed_across_different_guarantors()
    {
        var service = CreateService(new FakeFundRepository());
        await service.CreateFundAsync(Guid.NewGuid(), new CreateFundRequest
        {
            Name = "A",
            Type = FundType.Family,
            CommittedCapital = 100,
            JoinCode = "SHARECOD",
        });

        var fund = await service.CreateFundAsync(Guid.NewGuid(), new CreateFundRequest
        {
            Name = "B",
            Type = FundType.Family,
            CommittedCapital = 100,
            JoinCode = "SHARECOD",
        });

        fund.JoinCode.Should().Be("SHARECOD");
    }

    [Fact]
    public async Task Create_records_audit_event()
    {
        var repo = new FakeFundRepository();
        var audit = new FakeAuditLog();
        var service = new FundService(repo, audit);

        await service.CreateFundAsync(Guid.NewGuid(), new CreateFundRequest
        {
            Name = "Aunties Fund",
            Type = FundType.Family,
            CommittedCapital = 100,
        });

        audit.Events.Should().ContainSingle(e => e.Action == "Fund.Created");
    }

    [Fact]
    public async Task Get_for_guarantor_scopes_to_ownership()
    {
        var repo = new FakeFundRepository();
        var service = CreateService(repo);
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();

        var fund = await service.CreateFundAsync(owner, new CreateFundRequest
        {
            Name = "Mine",
            Type = FundType.Family,
            CommittedCapital = 100,
        });

        (await service.GetFundForGuarantorAsync(owner, fund.Id)).Should().NotBeNull();
        (await service.GetFundForGuarantorAsync(other, fund.Id)).Should().BeNull();
    }
}
