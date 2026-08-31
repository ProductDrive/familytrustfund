using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Loans;

public class LoanTests
{
    private static readonly Guid FundId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid GuarantorId = Guid.NewGuid();

    [Fact]
    public void Request_creates_pending_loan_with_correct_values()
    {
        var loan = Loan.Request(
            FundId,
            MemberId,
            500_000m,
            RepaymentFrequency.Monthly,
            0m,
            LoanFundingSource.GuarantorCapital,
            "School fees");

        loan.FundId.Should().Be(FundId);
        loan.MemberId.Should().Be(MemberId);
        loan.RequestedAmount.Should().Be(500_000m);
        loan.RequestedFrequency.Should().Be(RepaymentFrequency.Monthly);
        loan.InterestRate.Should().Be(0m);
        loan.Status.Should().Be(LoanStatus.Pending);
        loan.FundingSource.Should().Be(LoanFundingSource.GuarantorCapital);
        loan.Purpose.Should().Be("School fees");
        loan.GuarantorId.Should().BeNull();
        loan.ApprovedAmount.Should().BeNull();
        loan.OutstandingBalance.Should().Be(0m);
    }

    [Fact]
    public void Request_zero_amount_throws()
    {
        var act = () => Loan.Request(FundId, MemberId, 0m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        act.Should().Throw<InvalidLoanException>();
    }

    [Fact]
    public void Request_negative_amount_throws()
    {
        var act = () => Loan.Request(FundId, MemberId, -100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        act.Should().Throw<InvalidLoanException>();
    }

    [Fact]
    public void Request_overlong_purpose_throws()
    {
        var act = () => Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital, new string('x', 1001));
        act.Should().Throw<InvalidLoanException>();
    }

    [Fact]
    public void Request_trims_purpose()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital, "  School fees  ");
        loan.Purpose.Should().Be("School fees");
    }

    [Fact]
    public void Approve_sets_terms_and_freezes_status()
    {
        var loan = Loan.Request(FundId, MemberId, 500_000m, RepaymentFrequency.Monthly, 0m, LoanFundingSource.GuarantorCapital);

        loan.Approve(GuarantorId, 500_000m, RepaymentFrequency.Biweekly, 500_000m);

        loan.Status.Should().Be(LoanStatus.Approved);
        loan.GuarantorId.Should().Be(GuarantorId);
        loan.ApprovedAmount.Should().Be(500_000m);
        loan.ApprovedFrequency.Should().Be(RepaymentFrequency.Biweekly);
        loan.TotalRepayable.Should().Be(500_000m);
        loan.OutstandingBalance.Should().Be(500_000m);
        loan.ApprovedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Approve_non_pending_loan_throws()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loan.Reject(GuarantorId, "No");

        var act = () => loan.Approve(GuarantorId, 100m, RepaymentFrequency.Weekly, 100m);
        act.Should().Throw<InvalidLoanException>();
    }

    [Fact]
    public void Approve_zero_amount_throws()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);

        var act = () => loan.Approve(GuarantorId, 0m, RepaymentFrequency.Weekly, 0m);
        act.Should().Throw<InvalidLoanException>();
    }

    [Fact]
    public void Approve_total_repayable_less_than_amount_throws()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);

        var act = () => loan.Approve(GuarantorId, 100m, RepaymentFrequency.Weekly, 50m);
        act.Should().Throw<InvalidLoanException>();
    }

    [Fact]
    public void Reject_sets_status_and_reason()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);

        loan.Reject(GuarantorId, "Insufficient documentation");

        loan.Status.Should().Be(LoanStatus.Rejected);
        loan.GuarantorId.Should().Be(GuarantorId);
        loan.RejectionReason.Should().Be("Insufficient documentation");
        loan.RejectedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Reject_non_pending_loan_throws()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loan.Approve(GuarantorId, 100m, RepaymentFrequency.Weekly, 100m);

        var act = () => loan.Reject(GuarantorId, "No");
        act.Should().Throw<InvalidLoanException>();
    }

    [Fact]
    public void Reject_overlong_reason_throws()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);

        var act = () => loan.Reject(GuarantorId, new string('x', 1001));
        act.Should().Throw<InvalidLoanException>();
    }

    [Fact]
    public void Reject_trims_reason()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);

        loan.Reject(GuarantorId, "  No  ");

        loan.RejectionReason.Should().Be("No");
    }

    [Fact]
    public void MarkDisbursementPending_from_approved()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loan.Approve(GuarantorId, 100m, RepaymentFrequency.Weekly, 100m);

        loan.MarkDisbursementPending();

        loan.Status.Should().Be(LoanStatus.DisbursementPending);
    }

    [Fact]
    public void MarkDisbursed_from_disbursement_pending()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loan.Approve(GuarantorId, 100m, RepaymentFrequency.Weekly, 100m);
        loan.MarkDisbursementPending();

        loan.MarkDisbursed();

        loan.Status.Should().Be(LoanStatus.Disbursed);
    }

    [Fact]
    public void MarkDisbursed_from_approved_throws_without_pending()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loan.Approve(GuarantorId, 100m, RepaymentFrequency.Weekly, 100m);

        var act = () => loan.MarkDisbursed();
        act.Should().Throw<InvalidLoanException>();
    }

    [Fact]
    public void MarkCompleted_from_disbursed()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loan.Approve(GuarantorId, 100m, RepaymentFrequency.Weekly, 100m);
        loan.MarkDisbursementPending();
        loan.MarkDisbursed();

        loan.MarkCompleted();

        loan.Status.Should().Be(LoanStatus.Completed);
        loan.OutstandingBalance.Should().Be(0m);
    }

    [Fact]
    public void MarkDefaulted_from_disbursed()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loan.Approve(GuarantorId, 100m, RepaymentFrequency.Weekly, 100m);
        loan.MarkDisbursementPending();
        loan.MarkDisbursed();

        loan.MarkDefaulted();

        loan.Status.Should().Be(LoanStatus.Defaulted);
    }

    [Fact]
    public void ReduceOutstandingBalance_decreases_correctly()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loan.Approve(GuarantorId, 100m, RepaymentFrequency.Weekly, 100m);

        loan.ReduceOutstandingBalance(30m);

        loan.OutstandingBalance.Should().Be(70m);
    }

    [Fact]
    public void ReduceOutstandingBalance_exact_amount_sets_zero()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loan.Approve(GuarantorId, 100m, RepaymentFrequency.Weekly, 100m);

        loan.ReduceOutstandingBalance(100m);

        loan.OutstandingBalance.Should().Be(0m);
    }

    [Fact]
    public void ReduceOutstandingBalance_zero_throws()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loan.Approve(GuarantorId, 100m, RepaymentFrequency.Weekly, 100m);

        var act = () => loan.ReduceOutstandingBalance(0m);
        act.Should().Throw<InvalidLoanException>();
    }

    [Fact]
    public void ReduceOutstandingBalance_exceeding_throws()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loan.Approve(GuarantorId, 100m, RepaymentFrequency.Weekly, 100m);

        var act = () => loan.ReduceOutstandingBalance(101m);
        act.Should().Throw<InvalidLoanException>();
    }

    [Fact]
    public void IsActiveDisbursed_true_only_when_disbursed()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loan.IsActiveDisbursed.Should().BeFalse();

        loan.Approve(GuarantorId, 100m, RepaymentFrequency.Weekly, 100m);
        loan.IsActiveDisbursed.Should().BeFalse();

        loan.MarkDisbursementPending();
        loan.IsActiveDisbursed.Should().BeFalse();

        loan.MarkDisbursed();
        loan.IsActiveDisbursed.Should().BeTrue();
    }

    [Fact]
    public void IsPending_true_only_when_pending()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loan.IsPending.Should().BeTrue();

        loan.Approve(GuarantorId, 100m, RepaymentFrequency.Weekly, 100m);
        loan.IsPending.Should().BeFalse();
    }

    [Fact]
    public void Family_loan_has_zero_interest()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Monthly, 0m, LoanFundingSource.GuarantorCapital);
        loan.InterestRate.Should().Be(0m);
    }

    [Fact]
    public void External_loan_captures_interest_rate()
    {
        var loan = Loan.Request(FundId, MemberId, 100m, RepaymentFrequency.Monthly, 15m, LoanFundingSource.GuarantorCapital);
        loan.InterestRate.Should().Be(15m);
    }

    [Fact]
    public void Approved_amount_can_differ_from_requested()
    {
        var loan = Loan.Request(FundId, MemberId, 500_000m, RepaymentFrequency.Monthly, 0m, LoanFundingSource.GuarantorCapital);

        loan.Approve(GuarantorId, 300_000m, RepaymentFrequency.Biweekly, 300_000m);

        loan.RequestedAmount.Should().Be(500_000m);
        loan.ApprovedAmount.Should().Be(300_000m);
        loan.ApprovedFrequency.Should().Be(RepaymentFrequency.Biweekly);
        loan.RequestedFrequency.Should().Be(RepaymentFrequency.Monthly);
    }
}
