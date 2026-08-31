using FamilyTrustFund.Application.Membership;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Membership;
using FamilyTrustFund.Tests.Support;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Membership;

public class MembershipServiceTests
{
    private static (FakeFundRepository funds, FakeMembershipRepository members, MembershipService service) Setup()
    {
        var funds = new FakeFundRepository();
        var members = new FakeMembershipRepository();
        return (funds, members, new MembershipService(funds, members, new FakeAuditLog()));
    }

    private static Fund CreateFund(
        Guid guarantorId,
        string joinCode = "ABCD1234",
        FundStatus status = FundStatus.Active)
    {
        var fund = Fund.Create(guarantorId, "Aunties Fund", FundType.Family, 1_000_000m, joinCode);
        if (status == FundStatus.Inactive)
        {
            fund.SetStatus(FundStatus.Inactive);
        }

        return fund;
    }

    [Fact]
    public async Task Join_with_valid_code_creates_active_membership()
    {
        var (funds, members, service) = Setup();
        var guarantor = Guid.NewGuid();
        var member = Guid.NewGuid();
        funds.Funds.Add(CreateFund(guarantor));

        var result = await service.JoinFundAsync(member, new JoinFundRequest { JoinCode = "ABCD1234" });

        result.FundId.Should().Be(funds.Funds[0].Id);
        result.FundName.Should().Be("Aunties Fund");
        result.Status.Should().Be(MemberStatus.Active);
        members.Memberships.Should().ContainSingle(m => m.MemberId == member);
    }

    [Fact]
    public async Task Join_normalizes_join_code_case()
    {
        var (funds, _, service) = Setup();
        funds.Funds.Add(CreateFund(Guid.NewGuid()));

        var result = await service.JoinFundAsync(Guid.NewGuid(), new JoinFundRequest { JoinCode = "abcd1234" });

        result.FundName.Should().Be("Aunties Fund");
    }

    [Fact]
    public async Task Join_with_unknown_code_throws()
    {
        var (_, _, service) = Setup();

        var act = async () => await service.JoinFundAsync(
            Guid.NewGuid(), new JoinFundRequest { JoinCode = "NOPE1234" });

        await act.Should().ThrowAsync<InvalidMembershipException>();
    }

    [Fact]
    public async Task Join_inactive_fund_throws()
    {
        var (funds, _, service) = Setup();
        funds.Funds.Add(CreateFund(Guid.NewGuid(), status: FundStatus.Inactive));

        var act = async () => await service.JoinFundAsync(
            Guid.NewGuid(), new JoinFundRequest { JoinCode = "ABCD1234" });

        await act.Should().ThrowAsync<InvalidMembershipException>();
    }

    [Fact]
    public async Task Guarantor_cannot_join_own_fund()
    {
        var (funds, _, service) = Setup();
        var guarantor = Guid.NewGuid();
        funds.Funds.Add(CreateFund(guarantor));

        var act = async () => await service.JoinFundAsync(
            guarantor, new JoinFundRequest { JoinCode = "ABCD1234" });

        await act.Should().ThrowAsync<InvalidMembershipException>();
    }

    [Fact]
    public async Task Duplicate_join_throws()
    {
        var (funds, members, service) = Setup();
        var member = Guid.NewGuid();
        funds.Funds.Add(CreateFund(Guid.NewGuid()));
        await service.JoinFundAsync(member, new JoinFundRequest { JoinCode = "ABCD1234" });

        var act = async () => await service.JoinFundAsync(
            member, new JoinFundRequest { JoinCode = "ABCD1234" });

        await act.Should().ThrowAsync<InvalidMembershipException>();
        members.Memberships.Should().ContainSingle();
    }

    [Fact]
    public async Task Get_my_memberships_returns_member_funds()
    {
        var (funds, _, service) = Setup();
        var member = Guid.NewGuid();
        funds.Funds.Add(CreateFund(Guid.NewGuid()));
        await service.JoinFundAsync(member, new JoinFundRequest { JoinCode = "ABCD1234" });

        var mine = await service.GetMyMembershipsAsync(member);

        mine.Should().ContainSingle(m => m.FundName == "Aunties Fund");
    }

    [Fact]
    public async Task Join_records_audit_event()
    {
        var (funds, members, _) = Setup();
        var audit = new FakeAuditLog();
        var service = new MembershipService(funds, members, audit);
        var member = Guid.NewGuid();
        funds.Funds.Add(CreateFund(Guid.NewGuid()));

        await service.JoinFundAsync(member, new JoinFundRequest { JoinCode = "ABCD1234" });

        audit.Events.Should().Contain(e =>
            e.Action == "Membership.Joined"
            && e.ActorId == member
            && e.ResourceType == "Fund");
    }

    [Fact]
    public async Task Fund_members_are_scoped_to_owning_guarantor()
    {
        var (funds, members, service) = Setup();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var member = Guid.NewGuid();
        var fund = CreateFund(owner);
        funds.Funds.Add(fund);
        members.MembersByFund[fund.Id] = new List<FundMemberItem>
        {
            new() { MemberId = member, DisplayName = "Ada", Email = "ada@example.com", Status = MemberStatus.Active, JoinedAtUtc = DateTime.UtcNow },
        };

        var forOwner = await service.GetFundMembersAsync(owner, fund.Id);
        var forOther = await service.GetFundMembersAsync(other, fund.Id);

        forOwner.Should().NotBeNull();
        forOwner!.Should().ContainSingle(ki => ki.MemberId == member);
        forOther.Should().BeNull();
    }
}