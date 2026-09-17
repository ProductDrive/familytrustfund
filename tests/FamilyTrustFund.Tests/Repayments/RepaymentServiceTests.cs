using FamilyTrustFund.Application.Repayments;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Repayments;
using FamilyTrustFund.Tests.Support;
using FluentAssertions;
using Xunit;

namespace FamilyTrustFund.Tests.Repayments;

public class RepaymentRuleTests
{
    [Fact]
    public void Surplus_is_received_minus_expected()
    {
        RepaymentRules.Surplus(120_000m, 100_000m).Should().Be(20_000m);
        RepaymentRules.Surplus(95_000m, 100_000m).Should().Be(-5_000m);
        RepaymentRules.Surplus(100_000m, 100_000m).Should().Be(0m);
    }

    [Fact]
    public void Surplus_rejects_negative_received()
    {
        var act = () => RepaymentRules.Surplus(-1m, 0m);
        act.Should().Throw<InvalidRepaymentException>();
    }

    [Theory]
    [InlineData(RepaymentFrequency.Weekly)]
    [InlineData(RepaymentFrequency.Biweekly)]
    [InlineData(RepaymentFrequency.Monthly)]
    public void AddFrequency_advances_by_interval(RepaymentFrequency frequency)
    {
        var baseDate = new DateTime(2026, 1, 1);
        var result = RepaymentRules.AddFrequency(baseDate, frequency, 2);
        var expected = frequency switch
        {
            RepaymentFrequency.Weekly => baseDate.AddDays(14),
            RepaymentFrequency.Biweekly => baseDate.AddDays(28),
            _ => baseDate.AddMonths(2),
        };
        result.Should().Be(expected);
    }

    [Fact]
    public void BuildV1Instalments_split_total_across_term_with_exact_sum()
    {
        var items = RepaymentRules.BuildV1Instalments(
            Guid.NewGuid(), approvedAmount: 100_000m, totalRepayable: 110_000m,
            RepaymentFrequency.Monthly, term: 4, startDateUtc: new DateTime(2026, 1, 1));

        items.Count.Should().Be(4);
        items.Sum(i => i.ExpectedAmount).Should().Be(110_000m);
        items.Sum(i => i.PrincipalDue).Should().Be(100_000m);
        items.Sum(i => i.InterestDue).Should().Be(10_000m);
        items.First().Sequence.Should().Be(1);
        items.Last().Sequence.Should().Be(4);
        items[1].DueDateUtc.Should().Be(new DateTime(2026, 3, 1));
        items[3].DueDateUtc.Should().Be(new DateTime(2026, 5, 1));
    }

    [Fact]
    public void BuildV1Instalments_family_loan_has_no_interest()
    {
        var items = RepaymentRules.BuildV1Instalments(
            Guid.NewGuid(), 50_000m, 50_000m, RepaymentFrequency.Weekly, 2,
            new DateTime(2026, 1, 1));

        items.Should().OnlyContain(i => i.InterestDue == 0m);
        items.Sum(i => i.ExpectedAmount).Should().Be(50_000m);
    }

    [Fact]
    public void BuildAmortisedInstalments_reduces_each_instalment()
    {
        var items = RepaymentRules.BuildAmortisedInstalments(
            Guid.NewGuid(), 40_000m, 0m, 2, RepaymentFrequency.Monthly, new DateTime(2026, 1, 1));

        items.Count.Should().Be(2);
        items.Sum(i => i.PrincipalDue).Should().Be(40_000m);
    }

    [Fact]
    public void BuildAmortisedInstalments_keeps_remaining_interest()
    {
        var items = RepaymentRules.BuildAmortisedInstalments(
            Guid.NewGuid(), 35_000m, 7_500m, 3, RepaymentFrequency.Monthly, new DateTime(2026, 1, 1));

        items.Count.Should().Be(3);
        items.Sum(i => i.PrincipalDue).Should().Be(35_000m);
        items.Sum(i => i.InterestDue).Should().Be(7_500m);
        items.Sum(i => i.ExpectedAmount).Should().Be(42_500m);
    }
}

public class LoanScheduleTests
{
    [Fact]
    public void CreateV1_builds_expected_instalments()
    {
        var schedule = LoanSchedule.CreateV1(
            Guid.NewGuid(), 100_000m, 110_000m, RepaymentFrequency.Monthly, 4,
            new DateTime(2026, 1, 1));

        schedule.Version.Should().Be(1);
        schedule.Items.Count.Should().Be(4);
        schedule.Items.Sum(i => i.ExpectedAmount).Should().Be(110_000m);
    }

    [Fact]
    public void CreateV1_rejects_non_positive_term()
    {
        var act = () => LoanSchedule.CreateV1(
            Guid.NewGuid(), 100_000m, 110_000m, RepaymentFrequency.Monthly, 0);
        act.Should().Throw<InvalidRepaymentException>();
    }
}

public class RepaymentServiceTests
{
    private static readonly Guid FundId = Guid.NewGuid();
    private static readonly Guid GuarantorId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    private static Loan MakeDisbursedLoan(decimal amount = 100_000m, int term = 4)
    {
        return MakeDisbursedLoanFor(FundId, memberId: null, amount, term);
    }

    private static Loan MakeDisbursedLoanFor(Guid fundId, Guid? memberId = null, decimal amount = 100_000m, int term = 4)
    {
        var loan = Loan.Request(fundId, memberId ?? MemberId, amount, RepaymentFrequency.Monthly, 10m, LoanFundingSource.GuarantorCapital);
        var totalRepayable = amount + amount * 0.10m;
        loan.Approve(GuarantorId, amount, RepaymentFrequency.Monthly, totalRepayable, term);
        loan.MarkDisbursementPending();
        loan.MarkDisbursed();
        return loan;
    }

    private static (RepaymentService Service, FakeRepaymentRepository Repo) CreateService(Loan loan)
    {
        var repo = new FakeRepaymentRepository();
        repo.Loans.Add(loan);
        var audit = new FakeAuditLog();
        var service = new RepaymentService(repo, audit);
        return (service, repo);
    }

    [Fact]
    public async Task EnsureScheduleAsync_creates_v1_for_disbursed_loan()
    {
        var loan = MakeDisbursedLoan();
        var (service, _) = CreateService(loan);

        var schedule = await service.EnsureScheduleAsync(MemberId, loan.Id);

        schedule.Version.Should().Be(1);
        schedule.IsCurrent.Should().BeTrue();
        schedule.Items.Count.Should().Be(4);
    }

    [Fact]
    public async Task EnsureScheduleAsync_is_idempotent_when_revision_already_exists()
    {
        // Simulates the verify endpoint and the provider webhook racing to create
        // v1 for the same loan: the winner persists the schedule, so the loser's
        // insert must resolve to the existing schedule instead of a duplicate-key
        // violation (AGENTS §7.3, IX_loan_schedules_LoanId_Version stays unique).
        var loan = MakeDisbursedLoan();
        var repo = new FakeRepaymentRepository();
        repo.Loans.Add(loan);
        repo.Schedules.Add(LoanSchedule.CreateV1(
            loan.Id, loan.ApprovedAmount!.Value, loan.TotalRepayable,
            RepaymentFrequency.Monthly, 4, startDateUtc: DateTime.UtcNow));
        var service = new RepaymentService(repo, new FakeAuditLog());

        var schedule = await service.EnsureScheduleAsync(MemberId, loan.Id);
        var second = await service.EnsureScheduleAsync(MemberId, loan.Id);

        schedule.Id.Should().Be(second.Id);
        schedule.Version.Should().Be(1);
        repo.Schedules.Should().ContainSingle(s => s.LoanId == loan.Id);
    }

    [Fact]
    public async Task EnsureScheduleAsync_rejects_non_disbursed_loan()
    {
        var loan = Loan.Request(FundId, MemberId, 100_000m, RepaymentFrequency.Monthly, 0m, LoanFundingSource.GuarantorCapital);
        var (service, _) = CreateService(loan);

        var act = async () => await service.EnsureScheduleAsync(MemberId, loan.Id);
        await act.Should().ThrowAsync<InvalidRepaymentException>();
    }

    [Fact]
    public async Task Scheduled_payment_marks_item_paid_and_reduces_outstanding()
    {
        var loan = MakeDisbursedLoan();
        var (service, _) = CreateService(loan);
        await service.EnsureScheduleAsync(MemberId, loan.Id);
        var instalment = loan.TotalRepayable / 4m; // 27,500 = 25,000 principal + 2,500 interest

        var summary = await service.MakePaymentAsync(
            MemberId, new MakeRepaymentRequest { LoanId = loan.Id, Amount = instalment }, RepaymentKind.Scheduled);

        // Remaining obligation still includes the interest on the 3 future instalments.
        summary.OutstandingBalance.Should().Be(82_500m);
        summary.OutstandingInterest.Should().Be(7_500m);
        // Outstanding principal only reduces by this instalment's principal share.
        loan.OutstandingBalance.Should().Be(75_000m);
    }

    [Fact]
    public async Task Scheduled_payment_records_surplus_when_overpaid()
    {
        var loan = MakeDisbursedLoan();
        var (service, repo) = CreateService(loan);
        await service.EnsureScheduleAsync(MemberId, loan.Id);
        var instalment = loan.TotalRepayable / 4m; // 27,500

        await service.MakePaymentAsync(
            MemberId, new MakeRepaymentRequest { LoanId = loan.Id, Amount = instalment + 5_000m }, RepaymentKind.Scheduled);

        var repayment = repo.Repayments.Should().ContainSingle().Subject;
        repayment.Surplus.Should().Be(5_000m);
        repayment.Kind.Should().Be(RepaymentKind.Scheduled);
    }

    [Fact]
    public async Task Lump_sum_reduces_principal_and_keeps_remaining_interest()
    {
        var loan = MakeDisbursedLoan(); // 100k principal, 110k total, 4 instalments
        var (service, repo) = CreateService(loan);
        await service.EnsureScheduleAsync(MemberId, loan.Id);

        await service.MakePaymentAsync(
            MemberId, new MakeRepaymentRequest { LoanId = loan.Id, Amount = 40_000m }, RepaymentKind.LumpSum);

        // Lump-sum reduces principal only; the 10,000 total interest is
        // spread into the revised schedule, not discarded.
        loan.OutstandingBalance.Should().Be(60_000m);
        loan.Status.Should().Be(LoanStatus.Disbursed); // NOT completed — interest remains
        repo.Schedules.Count.Should().Be(2);
        var revision = repo.Schedules.Single(s => s.Version == 2);
        revision.RemainingObligation().Should().Be(70_000m); // 60k principal + 10k interest
        revision.RemainingInterest().Should().Be(10_000m);
        repo.Repayments.Should().ContainSingle(r => r.Kind == RepaymentKind.LumpSum);
    }

    [Fact]
    public async Task Lump_sum_paying_full_principal_but_not_interest_does_not_complete()
    {
        var loan = MakeDisbursedLoan(); // 100k principal, 110k total, 4 instalments
        var (service, repo) = CreateService(loan);
        await service.EnsureScheduleAsync(MemberId, loan.Id);

        await service.MakePaymentAsync(
            MemberId, new MakeRepaymentRequest { LoanId = loan.Id, Amount = 100_000m }, RepaymentKind.LumpSum);

        loan.OutstandingBalance.Should().Be(0m);
        loan.Status.Should().Be(LoanStatus.Disbursed);
        var revision = repo.Schedules.Single(s => s.Version == 2);
        revision.RemainingInterest().Should().Be(10_000m);
        revision.RemainingObligation().Should().Be(10_000m);
    }

    [Fact]
    public async Task Lump_sum_covering_full_obligation_completes_loan()
    {
        var loan = MakeDisbursedLoan(); // 100k principal, 110k total
        var (service, repo) = CreateService(loan);
        await service.EnsureScheduleAsync(MemberId, loan.Id);

        await service.MakePaymentAsync(
            MemberId, new MakeRepaymentRequest { LoanId = loan.Id, Amount = 110_000m }, RepaymentKind.LumpSum);

        loan.OutstandingBalance.Should().Be(0m);
        loan.Status.Should().Be(LoanStatus.Completed);
        repo.Repayments.Should().ContainSingle(r =>
            r.Kind == RepaymentKind.LumpSum && r.ExpectedAmount == 110_000m && r.Surplus == 0m);
    }

    [Fact]
    public async Task Summary_reflects_remaining_interest_in_quote_and_outstanding()
    {
        var loan = MakeDisbursedLoan(); // 110k obligation, 10k interest
        var (service, _) = CreateService(loan);
        await service.EnsureScheduleAsync(MemberId, loan.Id);

        var summary = await service.GetSummaryAsync(loan.Id);

        summary.SettlementQuote.Should().Be(110_000m);
        summary.OutstandingBalance.Should().Be(110_000m);
        summary.OutstandingInterest.Should().Be(10_000m);
    }

    [Fact]
    public async Task Settle_completes_loan_and_records_surplus()
    {
        var loan = MakeDisbursedLoan();
        var (service, repo) = CreateService(loan);
        await service.EnsureScheduleAsync(MemberId, loan.Id);

        var summary = await service.SettleAsync(
            MemberId, new MakeRepaymentRequest { LoanId = loan.Id, Amount = 110_000m });

        summary.OutstandingBalance.Should().Be(0m);
        loan.Status.Should().Be(LoanStatus.Completed);
        repo.Repayments.Should().ContainSingle(r =>
            r.Kind == RepaymentKind.FullSettlement && r.ExpectedAmount == 110_000m && r.Surplus == 0m);
    }

    [Fact]
    public async Task Settle_rejects_amount_below_outstanding()
    {
        var loan = MakeDisbursedLoan();
        var (service, _) = CreateService(loan);
        await service.EnsureScheduleAsync(MemberId, loan.Id);

        var act = async () => await service.SettleAsync(
            MemberId, new MakeRepaymentRequest { LoanId = loan.Id, Amount = 1_000m });
        await act.Should().ThrowAsync<InvalidRepaymentException>();
    }

    [Fact]
    public async Task Member_cannot_repay_another_members_loan()
    {
        var loan = MakeDisbursedLoan();
        var (service, _) = CreateService(loan);
        await service.EnsureScheduleAsync(MemberId, loan.Id);

        var otherMember = Guid.NewGuid();
        var act = async () => await service.MakePaymentAsync(
            otherMember, new MakeRepaymentRequest { LoanId = loan.Id, Amount = 10_000m }, RepaymentKind.Scheduled);
        await act.Should().ThrowAsync<InvalidRepaymentException>();
    }

    [Fact]
    public async Task Summary_detects_overdue_items()
    {
        var loan = MakeDisbursedLoan();
        var (service, _) = CreateService(loan);
        var schedule = LoanSchedule.CreateV1(
            loan.Id, loan.ApprovedAmount!.Value, loan.TotalRepayable,
            RepaymentFrequency.Monthly, 4, startDateUtc: DateTime.UtcNow.AddMonths(-10));
        // replace v1 schedule with an overdue one
        var repo = new FakeRepaymentRepository();
        repo.Loans.Add(loan);
        repo.Schedules.Add(schedule);
        var audit = new FakeAuditLog();
        var svc = new RepaymentService(repo, audit);

        var summary = await svc.GetSummaryAsync(loan.Id);

        summary.OverdueItems.Should().Be(4);
        summary.OverdueAmount.Should().Be(loan.TotalRepayable);
        summary.SettlementQuote.Should().Be(loan.TotalRepayable);
    }

    [Fact]
    public async Task GetMyRepayments_returns_paginated_history_across_funds()
    {
        var fundA = Fund.Create(GuarantorId, "Aunties Fund", FundType.Family, 1_000_000m, "AAAA");
        var fundB = Fund.Create(GuarantorId, "Uncles Fund", FundType.Family, 1_000_000m, "BBBB");
        var loanA = MakeDisbursedLoanFor(fundA.Id);
        var loanB = MakeDisbursedLoanFor(fundB.Id);

        var repo = new FakeRepaymentRepository();
        repo.Funds.Add(fundA);
        repo.Funds.Add(fundB);
        repo.Loans.Add(loanA);
        repo.Loans.Add(loanB);
        repo.Repayments.Add(new Repayment(loanA.Id, 1, RepaymentKind.Scheduled, 27_500m, 27_500m, new DateTime(2026, 1, 10)));
        repo.Repayments.Add(new Repayment(loanA.Id, 1, RepaymentKind.Scheduled, 27_500m, 30_000m, new DateTime(2026, 2, 10)));
        repo.Repayments.Add(new Repayment(loanB.Id, 1, RepaymentKind.FullSettlement, 55_000m, 55_000m, new DateTime(2026, 3, 10)));

        var service = new RepaymentService(repo, new FakeAuditLog());

        var page = await service.GetMyRepaymentsAsync(MemberId, page: 1, pageSize: 2);

        page.TotalCount.Should().Be(3);
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(2);
        page.Items.Count.Should().Be(2);
        page.Items.Should().BeInDescendingOrder(x => x.PaidAtUtc);
        page.Items[0].FundName.Should().Be("Uncles Fund");
        page.Items[1].FundName.Should().Be("Aunties Fund");

        var page2 = await service.GetMyRepaymentsAsync(MemberId, page: 2, pageSize: 2);

        page2.Items.Count.Should().Be(1);
        page2.Items[0].LoanId.Should().Be(loanA.Id);
        page2.Items[0].Surplus.Should().Be(0m);
    }

    [Fact]
    public async Task GetMyRepayments_excludes_other_members_records()
    {
        var loan = MakeDisbursedLoan();
        var otherFund = Fund.Create(GuarantorId, "Other Fund", FundType.Family, 1_000_000m, "CCCC");
        var other = MakeDisbursedLoanFor(otherFund.Id, memberId: Guid.NewGuid());

        var repo = new FakeRepaymentRepository();
        repo.Loans.Add(loan);
        repo.Loans.Add(other);
        repo.Repayments.Add(new Repayment(loan.Id, 1, RepaymentKind.Scheduled, 27_500m, 27_500m, new DateTime(2026, 1, 10)));
        repo.Repayments.Add(new Repayment(other.Id, 1, RepaymentKind.Scheduled, 27_500m, 27_500m, new DateTime(2026, 2, 10)));

        var service = new RepaymentService(repo, new FakeAuditLog());

        var page = await service.GetMyRepaymentsAsync(MemberId);

        page.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle(r => r.LoanId == loan.Id);
    }
}
