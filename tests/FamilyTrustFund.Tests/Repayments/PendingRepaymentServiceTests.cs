using FamilyTrustFund.Application.Evidence;
using FamilyTrustFund.Application.Loans;
using FamilyTrustFund.Application.Repayments;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Repayments;
using FamilyTrustFund.Tests.Support;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Repayments;

public class PendingRepaymentServiceTests
{
    private static readonly Guid GuarantorId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    private sealed class Harness
    {
        public Fund Fund { get; }
        public Loan Loan { get; }
        public FakeLoanRepository LoanRepo { get; } = new();
        public FakeFundRepository FundRepo { get; } = new();
        public FakeRepaymentRepository RepayRepo { get; } = new();
        public FakeEvidenceRepository EvidenceRepo { get; } = new();
        public FakePendingRepaymentRepository PendingRepo { get; } = new();
        public PendingRepaymentService Service { get; }

        public Harness(decimal amount = 100_000m, int term = 4)
        {
            Fund = Fund.Create(GuarantorId, "Aunties Fund", FundType.Family, 1_000_000m, "ABCD1234");
            FundRepo.Funds.Add(Fund);

            Loan = Loan.Request(Fund.Id, MemberId, amount, RepaymentFrequency.Monthly, 10m, LoanFundingSource.GuarantorCapital);
            var totalRepayable = amount + amount * 0.10m;
            Loan.Approve(GuarantorId, amount, RepaymentFrequency.Monthly, totalRepayable, term);
            Loan.MarkDisbursementPending();
            Loan.MarkDisbursed();

            // Same object reference so state propagates across repos.
            LoanRepo.Loans.Add(Loan);
            RepayRepo.Loans.Add(Loan);

            var repayService = new RepaymentService(RepayRepo, new FakeAuditLog());
            Service = new PendingRepaymentService(
                PendingRepo, LoanRepo, FundRepo, EvidenceRepo, repayService, new FakeAuditLog());
        }

        public async Task SeedScheduleAsync() =>
            await new RepaymentService(RepayRepo, new FakeAuditLog()).EnsureScheduleAsync(MemberId, Loan.Id);
    }

    [Fact]
    public async Task Report_creates_pending_for_owned_disbursed_loan()
    {
        var h = new Harness();
        await h.SeedScheduleAsync();

        var result = await h.Service.ReportAsync(MemberId, new ReportPendingRepaymentRequest
        {
            LoanId = h.Loan.Id,
            Amount = 27_500m,
            Kind = RepaymentKind.Scheduled,
            Reference = "FT900",
        });

        result.Status.Should().Be(PendingRepaymentStatus.PendingConfirmation);
        result.Amount.Should().Be(27_500m);
        result.FundName.Should().Be("Aunties Fund");
        h.PendingRepo.Items.Should().ContainSingle(p => p.LoanId == h.Loan.Id);
    }

    [Fact]
    public async Task Report_rejects_non_owned_loan()
    {
        var h = new Harness();
        var otherLoan = Loan.Request(h.Fund.Id, Guid.NewGuid(), 10_000m, RepaymentFrequency.Monthly, 0m, LoanFundingSource.GuarantorCapital);
        h.LoanRepo.Loans.Add(otherLoan);

        var act = async () => await h.Service.ReportAsync(MemberId, new ReportPendingRepaymentRequest
        {
            LoanId = otherLoan.Id,
            Amount = 100m,
            Kind = RepaymentKind.Scheduled,
        });

        await act.Should().ThrowAsync<InvalidRepaymentException>()
            .WithMessage("*own loans*");
    }

    [Fact]
    public async Task Report_rejects_non_disbursed_loan()
    {
        var h = new Harness();
        var pendingLoan = Loan.Request(h.Fund.Id, MemberId, 10_000m, RepaymentFrequency.Monthly, 0m, LoanFundingSource.GuarantorCapital);
        h.LoanRepo.Loans.Add(pendingLoan);

        var act = async () => await h.Service.ReportAsync(MemberId, new ReportPendingRepaymentRequest
        {
            LoanId = pendingLoan.Id,
            Amount = 100m,
            Kind = RepaymentKind.Scheduled,
        });

        await act.Should().ThrowAsync<InvalidRepaymentException>()
            .WithMessage("*disbursed*");
    }

    [Fact]
    public async Task Report_rejects_if_pending_exists_for_loan()
    {
        var h = new Harness();
        await h.SeedScheduleAsync();
        await h.Service.ReportAsync(MemberId, new ReportPendingRepaymentRequest
        {
            LoanId = h.Loan.Id,
            Amount = 27_500m,
            Kind = RepaymentKind.Scheduled,
        });

        var act = async () => await h.Service.ReportAsync(MemberId, new ReportPendingRepaymentRequest
        {
            LoanId = h.Loan.Id,
            Amount = 27_500m,
            Kind = RepaymentKind.Scheduled,
        });

        await act.Should().ThrowAsync<InvalidRepaymentException>()
            .WithMessage("*awaiting confirmation*");
    }

    [Fact]
    public async Task Confirm_posts_scheduled_repayment_and_marks_pending_confirmed()
    {
        var h = new Harness();
        await h.SeedScheduleAsync();
        var pending = await h.Service.ReportAsync(MemberId, new ReportPendingRepaymentRequest
        {
            LoanId = h.Loan.Id,
            Amount = 27_500m,
            Kind = RepaymentKind.Scheduled,
        });

        var result = await h.Service.ConfirmAsync(GuarantorId, new ConfirmPendingRepaymentRequest
        {
            PendingRepaymentId = pending.Id,
        });

        result.RepaymentPosted.Should().BeTrue();
        result.Pending.Status.Should().Be(PendingRepaymentStatus.Confirmed);
        h.RepayRepo.Repayments.Should().ContainSingle(r => r.LoanId == h.Loan.Id);
        // Instalment interest (2,500) is not principal; only the principal
        // share (25,000) reduces the outstanding balance.
        h.Loan.OutstandingBalance.Should().Be(75_000m);
    }

    [Fact]
    public async Task Confirm_round_trips_guarantor_confirmation_note()
    {
        var h = new Harness();
        await h.SeedScheduleAsync();
        var pending = await h.Service.ReportAsync(MemberId, new ReportPendingRepaymentRequest
        {
            LoanId = h.Loan.Id,
            Amount = 27_500m,
            Kind = RepaymentKind.Scheduled,
        });

        var result = await h.Service.ConfirmAsync(GuarantorId, new ConfirmPendingRepaymentRequest
        {
            PendingRepaymentId = pending.Id,
            Note = "Verified via statement",
        });

        result.Pending.ConfirmationNote.Should().Be("Verified via statement");
        h.PendingRepo.Items.Single().ConfirmationNote.Should().Be("Verified via statement");
    }

    [Fact]
    public async Task Confirm_full_settlement_posts_and_completes_loan()
    {
        var h = new Harness(amount: 50_000m, term: 2);
        await h.SeedScheduleAsync();
        var pending = await h.Service.ReportAsync(MemberId, new ReportPendingRepaymentRequest
        {
            LoanId = h.Loan.Id,
            Amount = 55_000m, // total repayable (100k principal + 10% = complete settlement quote)
            Kind = RepaymentKind.FullSettlement,
        });

        var result = await h.Service.ConfirmAsync(GuarantorId, new ConfirmPendingRepaymentRequest
        {
            PendingRepaymentId = pending.Id,
        });

        result.RepaymentPosted.Should().BeTrue();
        h.Loan.OutstandingBalance.Should().Be(0m);
        h.Loan.Status.Should().Be(LoanStatus.Completed);
        h.RepayRepo.Repayments.Should().ContainSingle(r => r.Kind == RepaymentKind.FullSettlement);
    }

    [Fact]
    public async Task Confirm_non_owner_guarantor_throws()
    {
        var h = new Harness();
        await h.SeedScheduleAsync();
        var pending = await h.Service.ReportAsync(MemberId, new ReportPendingRepaymentRequest
        {
            LoanId = h.Loan.Id,
            Amount = 27_500m,
            Kind = RepaymentKind.Scheduled,
        });

        var act = async () => await h.Service.ConfirmAsync(Guid.NewGuid(), new ConfirmPendingRepaymentRequest
        {
            PendingRepaymentId = pending.Id,
        });

        await act.Should().ThrowAsync<InvalidRepaymentException>()
            .WithMessage("*permission to confirm*");
    }

    [Fact]
    public async Task Confirm_is_idempotent_second_confirm_throws()
    {
        var h = new Harness();
        await h.SeedScheduleAsync();
        var pending = await h.Service.ReportAsync(MemberId, new ReportPendingRepaymentRequest
        {
            LoanId = h.Loan.Id,
            Amount = 27_500m,
            Kind = RepaymentKind.Scheduled,
        });

        await h.Service.ConfirmAsync(GuarantorId, new ConfirmPendingRepaymentRequest
        {
            PendingRepaymentId = pending.Id,
        });

        var act = async () => await h.Service.ConfirmAsync(GuarantorId, new ConfirmPendingRepaymentRequest
        {
            PendingRepaymentId = pending.Id,
        });

        await act.Should().ThrowAsync<InvalidRepaymentException>()
            .WithMessage("*Only a pending repayment*");
        h.RepayRepo.Repayments.Should().ContainSingle();
    }

    [Fact]
    public async Task Reject_does_not_post_and_marks_rejected()
    {
        var h = new Harness();
        await h.SeedScheduleAsync();
        var pending = await h.Service.ReportAsync(MemberId, new ReportPendingRepaymentRequest
        {
            LoanId = h.Loan.Id,
            Amount = 27_500m,
            Kind = RepaymentKind.Scheduled,
        });
        var balanceBefore = h.Loan.OutstandingBalance;

        var result = await h.Service.RejectAsync(GuarantorId, new RejectPendingRepaymentRequest
        {
            PendingRepaymentId = pending.Id,
            Reason = "Reference mismatch",
        });

        result.Status.Should().Be(PendingRepaymentStatus.Rejected);
        result.RejectionReason.Should().Be("Reference mismatch");
        h.RepayRepo.Repayments.Should().BeEmpty();
        h.Loan.OutstandingBalance.Should().Be(balanceBefore);
    }

    [Fact]
    public async Task Confirm_marks_attached_evidence_reviewed()
    {
        var h = new Harness();
        await h.SeedScheduleAsync();
        var pending = await h.Service.ReportAsync(MemberId, new ReportPendingRepaymentRequest
        {
            LoanId = h.Loan.Id,
            Amount = 27_500m,
            Kind = RepaymentKind.Scheduled,
        });

        var evidence = await new EvidenceService(h.EvidenceRepo, new FakeFileStorage(), new FakeAuditLog())
            .UploadAsync(MemberId, "Repayment", pending.Id, "receipt.png", "image/png", new byte[] { 1, 2, 3 });

        await h.Service.ConfirmAsync(GuarantorId, new ConfirmPendingRepaymentRequest
        {
            PendingRepaymentId = pending.Id,
        });

        h.EvidenceRepo.Items.Single(i => i.Id == evidence.Id)
            .ReviewedByUserId.Should().Be(GuarantorId);
    }
}
