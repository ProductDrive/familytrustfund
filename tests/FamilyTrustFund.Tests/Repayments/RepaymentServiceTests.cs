using FamilyTrustFund.Application.Repayments;
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
            Guid.NewGuid(), 40_000m, 2, RepaymentFrequency.Monthly, new DateTime(2026, 1, 1));

        items.Count.Should().Be(2);
        items.Sum(i => i.PrincipalDue).Should().Be(40_000m);
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
        var loan = Loan.Request(FundId, MemberId, amount, RepaymentFrequency.Monthly, 10m, LoanFundingSource.GuarantorCapital);
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
        var first = loan.ApprovedAmount!.Value / 4m + 10m; // 27,500 monthly instalment

        var summary = await service.MakePaymentAsync(
            MemberId, new MakeRepaymentRequest { LoanId = loan.Id, Amount = first }, RepaymentKind.Scheduled);

        summary.OutstandingBalance.Should().Be(loan.ApprovedAmount!.Value - first);
        loan.OutstandingBalance.Should().Be(loan.ApprovedAmount!.Value - first);
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
    public async Task Lump_sum_reduces_principal_and_creates_schedule_revision()
    {
        var loan = MakeDisbursedLoan();
        var (service, repo) = CreateService(loan);
        await service.EnsureScheduleAsync(MemberId, loan.Id);

        await service.MakePaymentAsync(
            MemberId, new MakeRepaymentRequest { LoanId = loan.Id, Amount = 40_000m }, RepaymentKind.LumpSum);

        loan.OutstandingBalance.Should().Be(60_000m);
        repo.Schedules.Count.Should().Be(2);
        repo.Schedules.Max(s => s.Version).Should().Be(2);
        repo.Repayments.Should().ContainSingle(r => r.Kind == RepaymentKind.LumpSum);
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
        repo.Repayments.Should().ContainSingle(r => r.Kind == RepaymentKind.FullSettlement);
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
        summary.SettlementQuote.Should().Be(loan.OutstandingBalance);
    }
}
