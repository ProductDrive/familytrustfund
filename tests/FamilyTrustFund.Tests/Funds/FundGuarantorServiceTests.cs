using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Tests.Support;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Funds;

/// <summary>
/// Group Guarantor membership, invitations and per-Guarantor committed capital
/// (ADR-051).
/// </summary>
public class FundGuarantorServiceTests
{
    private static readonly Guid OriginatorId = Guid.NewGuid();
    private static readonly Guid CoGuarantorId = Guid.NewGuid();

    private sealed class Harness
    {
        public FakeFundRepository Funds { get; } = new();
        public FakeFundGuarantorRepository Guarantors { get; }
        public FakeLoanRepository Loans { get; } = new();
        public FakeEmailSender Email { get; } = new();
        public FakeAuditLog Audit { get; } = new();
        public FundGuarantorService Service { get; }
        public Fund Fund { get; }

        public Harness(decimal committedCapital = 1_000_000m, FundGuarantorMode mode = FundGuarantorMode.Group)
        {
            Guarantors = new FakeFundGuarantorRepository(Funds);

            Fund = Fund.Create(
                OriginatorId,
                "Family Group",
                FundType.Family,
                committedCapital,
                "GROUP123",
                guarantorMode: mode);
            Funds.Add(Fund);

            if (mode == FundGuarantorMode.Group)
            {
                Guarantors.Add(FundGuarantor.CreateOriginator(Fund.Id, OriginatorId, committedCapital));
            }

            Service = new FundGuarantorService(
                Funds,
                Guarantors,
                new FakeFundAccessService(Funds, Guarantors),
                Loans,
                Audit,
                new GuarantorNotificationService(Email, Guarantors));
        }

        public void RegisterApprovedGuarantor(Guid id, string email, string name = "Guarantor")
        {
            Guarantors.Accounts.Add(new GuarantorAccount
            {
                Id = id,
                Email = email,
                DisplayName = name,
                IsApprovedGuarantor = true,
            });
            Guarantors.Profiles[id] = new GuarantorProfile
            {
                Id = id,
                Email = email,
                DisplayName = name,
            };
        }

        public async Task<FundGuarantorDto> InviteAsync(Guid id, decimal commitment = 500_000m)
        {
            RegisterApprovedGuarantor(id, $"{id:N}@example.com");
            return await Service.InviteAsync(OriginatorId, Fund.Id, new InviteGuarantorRequest
            {
                Email = $"{id:N}@example.com",
                CommittedCapital = commitment,
            });
        }

        public void AddPendingLoan()
        {
            Loans.Loans.Add(Loan.Request(
                Fund.Id,
                Guid.NewGuid(),
                50_000m,
                RepaymentFrequency.Monthly,
                0m,
                LoanFundingSource.GuarantorCapital,
                termMonths: 6,
                instalments: 6));
        }
    }

    [Fact]
    public async Task Invite_creates_an_open_invitation_without_changing_capital()
    {
        var h = new Harness();

        var invitation = await h.InviteAsync(CoGuarantorId, 500_000m);

        invitation.Status.Should().Be(FundGuarantorStatus.Invited);
        invitation.Role.Should().Be(FundGuarantorRole.Guarantor);
        invitation.CommittedCapital.Should().Be(500_000m);
        invitation.ExpiresAtUtc.Should().BeCloseTo(
            DateTime.UtcNow.AddDays(FundGuarantor.InvitationLifetimeDays), TimeSpan.FromMinutes(1));

        // Invitations do not add to the fund's committed capital until accepted.
        h.Fund.CommittedCapital.Should().Be(1_000_000m);
        h.Email.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task Invite_requires_an_existing_approved_guarantor_account()
    {
        var h = new Harness();

        var act = () => h.Service.InviteAsync(OriginatorId, h.Fund.Id, new InviteGuarantorRequest
        {
            Email = "stranger@example.com",
            CommittedCapital = 100_000m,
        });

        await act.Should().ThrowAsync<InvalidFundGuarantorException>()
            .WithMessage("*approved Guarantor*");
    }

    [Fact]
    public async Task Invite_rejects_an_unapproved_guarantor_account()
    {
        var h = new Harness();
        h.Guarantors.Accounts.Add(new GuarantorAccount
        {
            Id = CoGuarantorId,
            Email = "pending@example.com",
            DisplayName = "Pending",
            IsApprovedGuarantor = false,
        });

        var act = () => h.Service.InviteAsync(OriginatorId, h.Fund.Id, new InviteGuarantorRequest
        {
            Email = "pending@example.com",
            CommittedCapital = 100_000m,
        });

        await act.Should().ThrowAsync<InvalidFundGuarantorException>()
            .WithMessage("*approved Guarantor*");
    }

    [Fact]
    public async Task Originator_cannot_invite_themselves()
    {
        var h = new Harness();
        h.RegisterApprovedGuarantor(OriginatorId, "originator@example.com");

        var act = () => h.Service.InviteAsync(OriginatorId, h.Fund.Id, new InviteGuarantorRequest
        {
            Email = "originator@example.com",
            CommittedCapital = 100_000m,
        });

        await act.Should().ThrowAsync<InvalidFundGuarantorException>()
            .WithMessage("*cannot invite yourself*");
    }

    [Fact]
    public async Task Single_guarantor_fund_cannot_invite_guarantors()
    {
        var h = new Harness(mode: FundGuarantorMode.Single);
        h.RegisterApprovedGuarantor(CoGuarantorId, "co@example.com");

        var act = () => h.Service.InviteAsync(OriginatorId, h.Fund.Id, new InviteGuarantorRequest
        {
            Email = "co@example.com",
            CommittedCapital = 100_000m,
        });

        await act.Should().ThrowAsync<InvalidFundGuarantorException>()
            .WithMessage("*single-Guarantor fund*");
    }

    [Fact]
    public async Task Only_the_originator_can_invite()
    {
        var h = new Harness();
        await h.InviteAsync(CoGuarantorId);
        var membership = h.Guarantors.Memberships.Single(m => m.GuarantorId == CoGuarantorId);
        membership.Accept();
        var other = Guid.NewGuid();
        h.RegisterApprovedGuarantor(other, "other@example.com");

        var act = () => h.Service.InviteAsync(CoGuarantorId, h.Fund.Id, new InviteGuarantorRequest
        {
            Email = "other@example.com",
            CommittedCapital = 100_000m,
        });

        await act.Should().ThrowAsync<InvalidFundGuarantorException>()
            .WithMessage("*Fund Originator*");
    }

    [Fact]
    public async Task Duplicate_open_invitation_is_rejected()
    {
        var h = new Harness();
        await h.InviteAsync(CoGuarantorId);

        var act = () => h.InviteAsync(CoGuarantorId);

        await act.Should().ThrowAsync<InvalidFundGuarantorException>()
            .WithMessage("*already has an open invitation*");
    }

    [Fact]
    public async Task A_group_fund_allows_at_most_ten_active_guarantors()
    {
        var h = new Harness();
        // Originator plus nine accepted co-guarantors = ten active.
        for (var i = 0; i < 9; i++)
        {
            var id = Guid.NewGuid();
            var invitation = await h.InviteAsync(id, 10_000m);
            await h.Service.AcceptAsync(id, invitation.Id);
        }

        (await h.Guarantors.CountActiveAsync(h.Fund.Id)).Should().Be(10);

        var act = () => h.InviteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidFundGuarantorException>()
            .WithMessage("*at most 10*");
    }

    [Fact]
    public async Task Accepting_an_invitation_activates_the_guarantor_and_adds_capital()
    {
        var h = new Harness();
        var invitation = await h.InviteAsync(CoGuarantorId, 500_000m);

        var accepted = await h.Service.AcceptAsync(CoGuarantorId, invitation.Id);

        accepted.Status.Should().Be(FundGuarantorStatus.Active);
        h.Fund.CommittedCapital.Should().Be(1_500_000m);
        h.Guarantors.Memberships.Single(m => m.GuarantorId == CoGuarantorId)
            .IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Only_the_invited_guarantor_can_accept()
    {
        var h = new Harness();
        var invitation = await h.InviteAsync(CoGuarantorId);

        var act = () => h.Service.AcceptAsync(Guid.NewGuid(), invitation.Id);

        await act.Should().ThrowAsync<InvalidFundGuarantorException>()
            .WithMessage("*not yours*");
    }

    [Fact]
    public async Task Declining_an_invitation_does_not_change_capital()
    {
        var h = new Harness();
        var invitation = await h.InviteAsync(CoGuarantorId, 500_000m);

        var declined = await h.Service.DeclineAsync(CoGuarantorId, invitation.Id);

        declined.Status.Should().Be(FundGuarantorStatus.Declined);
        h.Fund.CommittedCapital.Should().Be(1_000_000m);
        h.Guarantors.Memberships.Single(m => m.GuarantorId == CoGuarantorId)
            .Status.Should().Be(FundGuarantorStatus.Declined);
    }

    [Fact]
    public async Task Expired_invitation_cannot_be_accepted()
    {
        var h = new Harness();
        var invitation = await h.InviteAsync(CoGuarantorId);
        var membership = h.Guarantors.Memberships.Single(m => m.Id == invitation.Id);

        typeof(FundGuarantor)
            .GetProperty(nameof(FundGuarantor.ExpiresAtUtc))!
            .SetValue(membership, DateTime.UtcNow.AddDays(-1));

        var act = () => h.Service.AcceptAsync(CoGuarantorId, invitation.Id);

        await act.Should().ThrowAsync<InvalidFundGuarantorException>()
            .WithMessage("*expired*");
        membership.Status.Should().Be(FundGuarantorStatus.Expired);
    }

    [Fact]
    public async Task Originator_can_remove_an_active_co_guarantor_and_capital_falls()
    {
        var h = new Harness();
        var invitation = await h.InviteAsync(CoGuarantorId, 500_000m);
        await h.Service.AcceptAsync(CoGuarantorId, invitation.Id);
        h.Fund.CommittedCapital.Should().Be(1_500_000m);

        var removed = await h.Service.RevokeAsync(OriginatorId, h.Fund.Id, CoGuarantorId);

        removed.Status.Should().Be(FundGuarantorStatus.Revoked);
        h.Fund.CommittedCapital.Should().Be(1_000_000m);
    }

    [Fact]
    public async Task Originator_cannot_remove_themselves()
    {
        var h = new Harness();

        var act = () => h.Service.RevokeAsync(OriginatorId, h.Fund.Id, OriginatorId);

        await act.Should().ThrowAsync<InvalidFundGuarantorException>()
            .WithMessage("*cannot be removed*");
    }

    [Fact]
    public async Task Co_guarantor_can_leave_but_the_originator_cannot()
    {
        var h = new Harness();
        var invitation = await h.InviteAsync(CoGuarantorId, 500_000m);
        await h.Service.AcceptAsync(CoGuarantorId, invitation.Id);

        var originatorLeave = () => h.Service.LeaveAsync(OriginatorId, h.Fund.Id);
        await originatorLeave.Should().ThrowAsync<InvalidFundGuarantorException>()
            .WithMessage("*cannot leave*");

        await h.Service.LeaveAsync(CoGuarantorId, h.Fund.Id);

        h.Guarantors.Memberships.Single(m => m.GuarantorId == CoGuarantorId)
            .Status.Should().Be(FundGuarantorStatus.Removed);
        h.Fund.CommittedCapital.Should().Be(1_000_000m);
    }

    [Fact]
    public async Task Committed_capital_cannot_be_reduced_below_active_disbursed_loans()
    {
        var h = new Harness();
        var invitation = await h.InviteAsync(CoGuarantorId, 500_000m);
        await h.Service.AcceptAsync(CoGuarantorId, invitation.Id);
        h.Loans.DisbursedTotals[h.Fund.Id] = 1_200_000m;

        var act = () => h.Service.UpdateCommitmentAsync(CoGuarantorId, h.Fund.Id,
            new UpdateGuarantorCommitmentRequest { CommittedCapital = 100_000m });

        await act.Should().ThrowAsync<InvalidFundException>()
            .WithMessage("*below the capital already disbursed*");
    }

    [Fact]
    public async Task Guarantor_can_increase_and_decrease_their_own_commitment()
    {
        var h = new Harness();
        var invitation = await h.InviteAsync(CoGuarantorId, 500_000m);
        await h.Service.AcceptAsync(CoGuarantorId, invitation.Id);

        await h.Service.UpdateCommitmentAsync(CoGuarantorId, h.Fund.Id,
            new UpdateGuarantorCommitmentRequest { CommittedCapital = 800_000m });
        h.Fund.CommittedCapital.Should().Be(1_800_000m);

        await h.Service.UpdateCommitmentAsync(CoGuarantorId, h.Fund.Id,
            new UpdateGuarantorCommitmentRequest { CommittedCapital = 200_000m });
        h.Fund.CommittedCapital.Should().Be(1_200_000m);
        h.Guarantors.Memberships.Single(m => m.GuarantorId == CoGuarantorId)
            .CommittedCapital.Should().Be(200_000m);
    }

    [Fact]
    public async Task Membership_cannot_change_while_a_loan_is_awaiting_decisions()
    {
        var h = new Harness();
        var invitation = await h.InviteAsync(CoGuarantorId, 500_000m);
        await h.Service.AcceptAsync(CoGuarantorId, invitation.Id);
        h.AddPendingLoan();

        var invite = () => h.InviteAsync(Guid.NewGuid());
        var revoke = () => h.Service.RevokeAsync(OriginatorId, h.Fund.Id, CoGuarantorId);
        var leave = () => h.Service.LeaveAsync(CoGuarantorId, h.Fund.Id);
        var commitment = () => h.Service.UpdateCommitmentAsync(CoGuarantorId, h.Fund.Id,
            new UpdateGuarantorCommitmentRequest { CommittedCapital = 700_000m });

        foreach (var act in new Func<Task>[] { invite, revoke, leave, commitment })
        {
            await act.Should().ThrowAsync<InvalidFundGuarantorException>()
                .WithMessage("*awaiting decisions*");
        }
    }

    [Fact]
    public async Task Guarantor_list_is_only_visible_to_fund_operators()
    {
        var h = new Harness();
        await h.InviteAsync(CoGuarantorId);

        (await h.Service.GetFundGuarantorsAsync(OriginatorId, h.Fund.Id)).Should().NotBeNull();
        (await h.Service.GetFundGuarantorsAsync(Guid.NewGuid(), h.Fund.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Invitation_list_returns_the_callers_invitations()
    {
        var h = new Harness();
        await h.InviteAsync(CoGuarantorId, 500_000m);

        var mine = await h.Service.GetMineAsync(CoGuarantorId);

        mine.Should().ContainSingle();
        mine[0].FundId.Should().Be(h.Fund.Id);
        mine[0].OriginatorId.Should().Be(OriginatorId);
        mine[0].Status.Should().Be(FundGuarantorStatus.Invited);
    }
}
