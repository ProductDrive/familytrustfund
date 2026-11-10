using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Funds;

/// <summary>
/// Domain rules for Group Guarantor memberships, invitations and the aggregate
/// committed capital they feed (ADR-051).
/// </summary>
public class FundGuarantorTests
{
    private static readonly Guid FundId = Guid.NewGuid();
    private static readonly Guid OriginatorId = Guid.NewGuid();
    private static readonly Guid GuarantorId = Guid.NewGuid();

    [Fact]
    public void CreateOriginator_is_active_with_no_expiry()
    {
        var membership = FundGuarantor.CreateOriginator(FundId, OriginatorId, 1_000_000m);

        membership.Role.Should().Be(FundGuarantorRole.Originator);
        membership.Status.Should().Be(FundGuarantorStatus.Active);
        membership.IsOriginator.Should().BeTrue();
        membership.IsActive.Should().BeTrue();
        membership.ExpiresAtUtc.Should().BeNull();
        membership.CommittedCapital.Should().Be(1_000_000m);
    }

    [Fact]
    public void Invite_sets_a_fourteen_day_expiry_and_normalises_email()
    {
        var membership = FundGuarantor.Invite(FundId, GuarantorId, " Co@Example.COM ", 500_000m, OriginatorId);

        membership.Status.Should().Be(FundGuarantorStatus.Invited);
        membership.Role.Should().Be(FundGuarantorRole.Guarantor);
        membership.InvitedEmail.Should().Be("co@example.com");
        membership.IsOpenInvitation.Should().BeTrue();
        membership.IsExpired.Should().BeFalse();
        membership.ExpiresAtUtc.Should().BeCloseTo(
            DateTime.UtcNow.AddDays(FundGuarantor.InvitationLifetimeDays), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Invite_requires_an_email_and_a_non_negative_commitment()
    {
        var noEmail = () => FundGuarantor.Invite(FundId, GuarantorId, " ", 100m, OriginatorId);
        var negative = () => FundGuarantor.Invite(FundId, GuarantorId, "co@example.com", -1m, OriginatorId);

        noEmail.Should().Throw<InvalidFundGuarantorException>();
        negative.Should().Throw<InvalidFundGuarantorException>();
    }

    [Fact]
    public void Accept_activates_the_membership_and_clears_the_expiry()
    {
        var membership = FundGuarantor.Invite(FundId, GuarantorId, "co@example.com", 100m, OriginatorId);

        membership.Accept();

        membership.Status.Should().Be(FundGuarantorStatus.Active);
        membership.IsActive.Should().BeTrue();
        membership.ExpiresAtUtc.Should().BeNull();
        membership.RespondedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Declined_invitation_cannot_be_accepted()
    {
        var membership = FundGuarantor.Invite(FundId, GuarantorId, "co@example.com", 100m, OriginatorId);
        membership.Decline();

        var act = () => membership.Accept();

        act.Should().Throw<InvalidFundGuarantorException>();
        membership.Status.Should().Be(FundGuarantorStatus.Declined);
    }

    [Fact]
    public void Expired_invitation_cannot_be_accepted()
    {
        var membership = FundGuarantor.Invite(FundId, GuarantorId, "co@example.com", 100m, OriginatorId);
        typeof(FundGuarantor)
            .GetProperty(nameof(FundGuarantor.ExpiresAtUtc))!
            .SetValue(membership, DateTime.UtcNow.AddMinutes(-1));

        var act = () => membership.Accept();

        act.Should().Throw<InvalidFundGuarantorException>()
            .WithMessage("*expired*");
    }

    [Fact]
    public void MarkExpired_only_applies_to_an_open_invitation()
    {
        var membership = FundGuarantor.Invite(FundId, GuarantorId, "co@example.com", 100m, OriginatorId);

        membership.MarkExpired();
        membership.Status.Should().Be(FundGuarantorStatus.Expired);

        var act = () => membership.MarkExpired();
        act.Should().Throw<InvalidFundGuarantorException>();
    }

    [Fact]
    public void Originator_cannot_be_revoked_or_leave()
    {
        var originator = FundGuarantor.CreateOriginator(FundId, OriginatorId, 1_000_000m);

        var revoke = () => originator.Revoke();
        var leave = () => originator.Leave();

        revoke.Should().Throw<InvalidFundGuarantorException>();
        leave.Should().Throw<InvalidFundGuarantorException>();
        originator.Status.Should().Be(FundGuarantorStatus.Active);
    }

    [Fact]
    public void Active_co_guarantor_can_leave_and_cannot_leave_twice()
    {
        var membership = FundGuarantor.Invite(FundId, GuarantorId, "co@example.com", 100m, OriginatorId);
        membership.Accept();

        membership.Leave();

        membership.Status.Should().Be(FundGuarantorStatus.Removed);
        var act = () => membership.Leave();
        act.Should().Throw<InvalidFundGuarantorException>();
    }

    [Fact]
    public void ReinstateInvitation_reopens_a_declined_invitation()
    {
        var membership = FundGuarantor.Invite(FundId, GuarantorId, "co@example.com", 100m, OriginatorId);
        membership.Decline();

        membership.ReinstateInvitation(250_000m, OriginatorId);

        membership.Status.Should().Be(FundGuarantorStatus.Invited);
        membership.CommittedCapital.Should().Be(250_000m);
        membership.RespondedAtUtc.Should().BeNull();
        membership.ExpiresAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void ReinstateInvitation_cannot_reopen_an_active_membership()
    {
        var membership = FundGuarantor.Invite(FundId, GuarantorId, "co@example.com", 100m, OriginatorId);
        membership.Accept();

        var act = () => membership.ReinstateInvitation(250_000m, OriginatorId);

        act.Should().Throw<InvalidFundGuarantorException>();
    }

    [Fact]
    public void Fund_defaults_to_single_guarantor_mode()
    {
        var fund = Fund.Create(OriginatorId, "Solo", FundType.Family, 100m, "SOLO1234");

        fund.GuarantorMode.Should().Be(FundGuarantorMode.Single);
        fund.IsGroupFund.Should().BeFalse();
    }

    [Fact]
    public void Group_fund_mode_is_captured_at_creation()
    {
        var fund = Fund.Create(
            OriginatorId, "Group", FundType.Family, 100m, "GROUP123",
            guarantorMode: FundGuarantorMode.Group);

        fund.IsGroupFund.Should().BeTrue();
    }

    [Fact]
    public void Increase_and_decrease_committed_capital_adjust_the_aggregate()
    {
        var fund = Fund.Create(
            OriginatorId, "Group", FundType.Family, 1_000_000m, "GROUP123",
            guarantorMode: FundGuarantorMode.Group);

        fund.IncreaseCommittedCapital(500_000m);
        fund.CommittedCapital.Should().Be(1_500_000m);

        fund.DecreaseCommittedCapital(200_000m, activeDisbursedLoans: 300_000m);
        fund.CommittedCapital.Should().Be(1_300_000m);
    }

    [Fact]
    public void Committed_capital_cannot_fall_below_active_disbursed_loans()
    {
        var fund = Fund.Create(
            OriginatorId, "Group", FundType.Family, 1_000_000m, "GROUP123",
            guarantorMode: FundGuarantorMode.Group);

        var act = () => fund.DecreaseCommittedCapital(800_000m, activeDisbursedLoans: 500_000m);

        act.Should().Throw<InvalidFundException>()
            .WithMessage("*below the capital already disbursed*");
        fund.CommittedCapital.Should().Be(1_000_000m);
    }

    [Fact]
    public void Increase_requires_a_positive_amount()
    {
        var fund = Fund.Create(
            OriginatorId, "Group", FundType.Family, 1_000_000m, "GROUP123",
            guarantorMode: FundGuarantorMode.Group);

        var act = () => fund.IncreaseCommittedCapital(0m);

        act.Should().Throw<InvalidFundException>();
    }

    [Fact]
    public void LoanApproval_cannot_record_a_second_decision()
    {
        var approval = LoanApproval.Require(Guid.NewGuid(), GuarantorId);

        approval.Approve("Fine");

        approval.IsApproved.Should().BeTrue();
        approval.DecidedAtUtc.Should().NotBeNull();
        approval.Comment.Should().Be("Fine");

        var second = () => approval.Reject("Changed my mind");
        second.Should().Throw<InvalidLoanException>()
            .WithMessage("*already recorded*");
    }

    [Fact]
    public void Cancelled_approval_does_not_override_a_recorded_decision()
    {
        var approval = LoanApproval.Require(Guid.NewGuid(), GuarantorId);
        approval.Approve();

        approval.Cancel();

        approval.Status.Should().Be(LoanApprovalStatus.Approved);
    }
}
