using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Application.Loans;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Membership;
using FamilyTrustFund.Tests.Support;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Loans;

/// <summary>
/// Unanimous Group Guarantor loan approval (ADR-052): one attributable decision
/// per active Guarantor, no duplicate decisions, immediate rejection, and
/// originator-owned terms.
/// </summary>
public class GroupLoanApprovalTests
{
    private static readonly Guid OriginatorId = Guid.NewGuid();
    private static readonly Guid CoGuarantorId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid OutsiderId = Guid.NewGuid();

    private sealed class Harness
    {
        public FakeFundRepository Funds { get; } = new();
        public FakeMembershipRepository Members { get; } = new();
        public FakeLoanRepository Loans { get; } = new();
        public FakeContributionRepository Contributions { get; } = new();
        public FakeFundGuarantorRepository Guarantors { get; }
        public FakeEmailSender Email { get; } = new();
        public FakeAuditLog Audit { get; } = new();
        public LoanService Service { get; }
        public Fund Fund { get; }

        public Harness(decimal committedCapital = 1_000_000m)
        {
            Guarantors = new FakeFundGuarantorRepository(Funds);

            Fund = Fund.Create(
                OriginatorId,
                "Family Group",
                FundType.Family,
                committedCapital,
                "GROUP123",
                guarantorMode: FundGuarantorMode.Group);
            Funds.Add(Fund);

            Guarantors.Add(FundGuarantor.CreateOriginator(Fund.Id, OriginatorId, committedCapital));
            var invite = FundGuarantor.Invite(Fund.Id, CoGuarantorId, "co@example.com", 0m, OriginatorId);
            Guarantors.Add(invite);
            invite.Accept();

            Members.Memberships.Add(FundMember.Join(Fund.Id, MemberId));
            Contributions.ConfirmedFundCredits[(MemberId, Fund.Id)] = 200_000m;

            Service = new LoanService(
                Loans,
                Funds,
                Members,
                Contributions,
                new FakeFundAccessService(Funds, Guarantors),
                Audit,
                new GuarantorNotificationService(Email, Guarantors));
        }

        public Task<LoanDto> RequestAsync(decimal amount = 100_000m) =>
            Service.RequestLoanAsync(MemberId, new RequestLoanRequest
            {
                FundId = Fund.Id,
                Amount = amount,
                Frequency = RepaymentFrequency.Monthly,
                TermMonths = 12,
                Purpose = "School fees",
            });

        public Task<LoanDto> ApproveAsync(Guid guarantorId, Guid loanId, decimal? amount = null) =>
            Service.ApproveLoanAsync(guarantorId, new ApproveLoanRequest
            {
                LoanId = loanId,
                ApprovedAmount = amount ?? 0m,
                ConfirmedWithMember = amount is not null,
            });

        public Task<LoanDto> RejectAsync(Guid guarantorId, Guid loanId) =>
            Service.RejectLoanAsync(guarantorId, new RejectLoanRequest
            {
                LoanId = loanId,
                Reason = "Not affordable",
            });
    }

    [Fact]
    public async Task Request_creates_one_pending_approval_per_active_guarantor()
    {
        var h = new Harness();

        var loan = await h.RequestAsync();

        loan.Status.Should().Be(LoanStatus.Pending);
        loan.RequiredApprovalCount.Should().Be(2);
        loan.ApprovedApprovalCount.Should().Be(0);
        loan.TermsFrozen.Should().BeFalse();
        loan.Approvals.Should().HaveCount(2);
        loan.Approvals.Select(a => a.GuarantorId)
            .Should().BeEquivalentTo(new[] { OriginatorId, CoGuarantorId });
        loan.Approvals.Should().OnlyContain(a => a.Status == LoanApprovalStatus.Pending);
    }

    [Fact]
    public async Task Loan_stays_pending_until_every_guarantor_approves()
    {
        var h = new Harness();
        var requested = await h.RequestAsync();

        var afterFirst = await h.ApproveAsync(OriginatorId, requested.Id);

        afterFirst.Status.Should().Be(LoanStatus.Pending);
        afterFirst.TermsFrozen.Should().BeTrue();
        afterFirst.ApprovedApprovalCount.Should().Be(1);
        afterFirst.RequiredApprovalCount.Should().Be(2);

        var afterSecond = await h.ApproveAsync(CoGuarantorId, requested.Id);

        afterSecond.Status.Should().Be(LoanStatus.Approved);
        afterSecond.ApprovedApprovalCount.Should().Be(2);
        afterSecond.ApprovedAmount.Should().Be(100_000m);
        afterSecond.ApprovedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Guarantor_cannot_record_a_second_decision()
    {
        var h = new Harness();
        var requested = await h.RequestAsync();
        await h.ApproveAsync(OriginatorId, requested.Id);

        var act = () => h.ApproveAsync(OriginatorId, requested.Id);

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*already recorded*");
    }

    [Fact]
    public async Task Guarantor_cannot_approve_then_reject()
    {
        var h = new Harness();
        var requested = await h.RequestAsync();
        await h.ApproveAsync(OriginatorId, requested.Id);

        var act = () => h.RejectAsync(OriginatorId, requested.Id);

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*already recorded*");
    }

    [Fact]
    public async Task Any_single_rejection_rejects_the_loan_and_closes_other_decisions()
    {
        var h = new Harness();
        var requested = await h.RequestAsync();
        await h.ApproveAsync(OriginatorId, requested.Id);

        var rejected = await h.RejectAsync(CoGuarantorId, requested.Id);

        rejected.Status.Should().Be(LoanStatus.Rejected);
        rejected.RejectionReason.Should().Be("Not affordable");

        var approvals = h.Loans.Approvals.Where(a => a.LoanId == requested.Id).ToList();
        approvals.Single(a => a.GuarantorId == CoGuarantorId).Status
            .Should().Be(LoanApprovalStatus.Rejected);
        // The originator's earlier approval stands; nothing is left pending.
        approvals.Single(a => a.GuarantorId == OriginatorId).Status
            .Should().Be(LoanApprovalStatus.Approved);
        approvals.Should().NotContain(a => a.IsPending);
    }

    [Fact]
    public async Task Rejection_after_partial_approval_cancels_remaining_pending_decisions()
    {
        var h = new Harness();
        var requested = await h.RequestAsync();

        await h.RejectAsync(OriginatorId, requested.Id);

        h.Loans.Approvals
            .Where(a => a.LoanId == requested.Id)
            .Should().OnlyContain(a => a.Status != LoanApprovalStatus.Pending);
        h.Loans.Loans.Single().Status.Should().Be(LoanStatus.Rejected);
    }

    [Fact]
    public async Task Co_guarantor_cannot_change_loan_terms()
    {
        var h = new Harness();
        var requested = await h.RequestAsync();

        var act = () => h.ApproveAsync(CoGuarantorId, requested.Id, amount: 80_000m);

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*Only the Fund Originator can change*");
    }

    [Fact]
    public async Task Co_guarantor_can_freeze_the_members_requested_terms()
    {
        var h = new Harness();
        var requested = await h.RequestAsync();

        var afterCo = await h.ApproveAsync(CoGuarantorId, requested.Id);

        afterCo.TermsFrozen.Should().BeTrue();
        afterCo.Status.Should().Be(LoanStatus.Pending);
        afterCo.ApprovedAmount.Should().Be(100_000m);
    }

    [Fact]
    public async Task Originator_changed_terms_are_frozen_and_voted_on_by_co_guarantor()
    {
        var h = new Harness();
        var requested = await h.RequestAsync();

        var afterOriginator = await h.ApproveAsync(OriginatorId, requested.Id, amount: 80_000m);
        afterOriginator.TermsFrozen.Should().BeTrue();
        afterOriginator.ApprovedAmount.Should().Be(80_000m);
        afterOriginator.Status.Should().Be(LoanStatus.Pending);

        // The co-guarantor can only vote yes/no on the frozen terms; supplying
        // different terms would be ignored because the terms are already fixed.
        var afterCo = await h.ApproveAsync(CoGuarantorId, requested.Id);

        afterCo.Status.Should().Be(LoanStatus.Approved);
        afterCo.ApprovedAmount.Should().Be(80_000m);
    }

    [Fact]
    public async Task Non_guarantor_cannot_approve_or_reject()
    {
        var h = new Harness();
        var requested = await h.RequestAsync();

        var approve = () => h.ApproveAsync(OutsiderId, requested.Id);
        var reject = () => h.RejectAsync(OutsiderId, requested.Id);

        await approve.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*permission*");
        await reject.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*permission*");
    }

    [Fact]
    public async Task Only_the_originator_can_cancel_a_group_loan()
    {
        var h = new Harness();
        var requested = await h.RequestAsync();

        var byCo = () => h.Service.CancelLoanByGuarantorAsync(CoGuarantorId, requested.Id);
        await byCo.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*Fund Originator*");

        var cancelled = await h.Service.CancelLoanByGuarantorAsync(OriginatorId, requested.Id);

        cancelled.Status.Should().Be(LoanStatus.Cancelled);
        h.Loans.Approvals
            .Where(a => a.LoanId == requested.Id)
            .Should().OnlyContain(a => a.Status == LoanApprovalStatus.Cancelled);
    }

    [Fact]
    public async Task Pending_queue_only_lists_loans_awaiting_the_callers_decision()
    {
        var h = new Harness();
        var requested = await h.RequestAsync();

        (await h.Service.GetPendingRequestsForGuarantorAsync(OriginatorId))
            .Should().ContainSingle(l => l.Id == requested.Id);

        await h.ApproveAsync(OriginatorId, requested.Id);

        (await h.Service.GetPendingRequestsForGuarantorAsync(OriginatorId))
            .Should().BeEmpty();
        (await h.Service.GetPendingRequestsForGuarantorAsync(CoGuarantorId))
            .Should().ContainSingle(l => l.Id == requested.Id);
    }

    [Fact]
    public async Task Approvers_are_snapshotted_at_request_time()
    {
        var h = new Harness();
        var requested = await h.RequestAsync();

        // A new Guarantor joining after the request must not change who must
        // decide the existing loan.
        var lateJoinerId = Guid.NewGuid();
        var invite = FundGuarantor.Invite(h.Fund.Id, lateJoinerId, "late@example.com", 0m, OriginatorId);
        h.Guarantors.Add(invite);
        invite.Accept();

        var afterBoth = await h.ApproveAsync(OriginatorId, requested.Id);
        afterBoth.RequiredApprovalCount.Should().Be(2);

        var approved = await h.ApproveAsync(CoGuarantorId, requested.Id);
        approved.Status.Should().Be(LoanStatus.Approved);
        approved.RequiredApprovalCount.Should().Be(2);
    }

    [Fact]
    public async Task Legacy_single_guarantor_loan_without_approval_rows_can_still_be_approved()
    {
        var funds = new FakeFundRepository();
        var members = new FakeMembershipRepository();
        var loans = new FakeLoanRepository();
        var contributions = new FakeContributionRepository();
        var guarantors = new FakeFundGuarantorRepository(funds);
        var service = new LoanService(
            loans,
            funds,
            members,
            contributions,
            new FakeFundAccessService(funds, guarantors),
            new FakeAuditLog(),
            new GuarantorNotificationService(new FakeEmailSender(), guarantors));

        var fund = Fund.Create(OriginatorId, "Legacy Fund", FundType.Family, 1_000_000m, "LEGACY01");
        funds.Add(fund);
        members.Memberships.Add(FundMember.Join(fund.Id, MemberId));
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        var loan = Loan.Request(
            fund.Id, MemberId, 100_000m, RepaymentFrequency.Monthly, 0m,
            LoanFundingSource.GuarantorCapital, termMonths: 12, instalments: 12);
        loans.Loans.Add(loan);

        var approved = await service.ApproveLoanAsync(OriginatorId, new ApproveLoanRequest
        {
            LoanId = loan.Id,
        });

        approved.Status.Should().Be(LoanStatus.Approved);
        loans.Approvals.Should().ContainSingle(a => a.GuarantorId == OriginatorId);
    }
}
