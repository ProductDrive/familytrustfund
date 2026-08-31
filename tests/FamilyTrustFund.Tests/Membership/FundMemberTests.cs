using FamilyTrustFund.Domain.Membership;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Membership;

public class FundMemberTests
{
    [Fact]
    public void Join_creates_active_membership()
    {
        var fundId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var membership = FundMember.Join(fundId, memberId);

        membership.Id.Should().NotBeEmpty();
        membership.FundId.Should().Be(fundId);
        membership.MemberId.Should().Be(memberId);
        membership.Status.Should().Be(MemberStatus.Active);
        membership.JoinedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Join_requires_fund()
    {
        var act = () => FundMember.Join(Guid.Empty, Guid.NewGuid());
        act.Should().Throw<InvalidMembershipException>();
    }

    [Fact]
    public void Join_requires_member()
    {
        var act = () => FundMember.Join(Guid.NewGuid(), Guid.Empty);
        act.Should().Throw<InvalidMembershipException>();
    }

    [Fact]
    public void Suspend_and_reactivate_cycle()
    {
        var membership = FundMember.Join(Guid.NewGuid(), Guid.NewGuid());

        membership.Suspend();
        membership.Status.Should().Be(MemberStatus.Suspended);

        membership.Reactivate();
        membership.Status.Should().Be(MemberStatus.Active);
    }

    [Fact]
    public void Double_suspend_throws()
    {
        var membership = FundMember.Join(Guid.NewGuid(), Guid.NewGuid());
        membership.Suspend();
        var second = () => membership.Suspend();
        second.Should().Throw<InvalidMembershipException>();
    }

    [Fact]
    public void Double_reactivate_throws()
    {
        var membership = FundMember.Join(Guid.NewGuid(), Guid.NewGuid());
        var second = () => membership.Reactivate();
        second.Should().Throw<InvalidMembershipException>();
    }
}