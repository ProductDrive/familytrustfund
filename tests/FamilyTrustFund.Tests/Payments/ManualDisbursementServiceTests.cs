using FamilyTrustFund.Application.Evidence;
using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Application.Loans;
using FamilyTrustFund.Application.Payments;
using FamilyTrustFund.Application.Repayments;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Payments;
using FamilyTrustFund.Tests.Support;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Payments;

/// <summary>
/// Application-service tests for the offline/manual disbursement workflow
/// (ADR-056): the Fund Originator reports and submits, evidence is required,
/// the member confirms (which disburses the loan and records manual capital) or
/// disputes, and no other disbursement path may run in parallel.
/// </summary>
public class ManualDisbursementServiceTests
{
    private static readonly Guid GuarantorId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    private sealed class Harness
    {
        public Fund Fund { get; }
        public Loan Loan { get; }
        public FakeFundRepository FundRepo { get; } = new();
        public FakeLoanRepository LoanRepo { get; } = new();
        public FakeRepaymentRepository RepayRepo { get; } = new();
        public FakeEvidenceRepository EvidenceRepo { get; } = new();
        public FakeManualDisbursementRepository ManualRepo { get; } = new();
        public FakeCapitalFundingRepository CapitalRepo { get; } = new();
        public FakePaymentRepository PaymentRepo { get; } = new();
        public FakeFundGuarantorRepository Guarantors { get; }
        public Guid? CoGuarantorId { get; }
        public ManualDisbursementService Service { get; }

        public Harness(decimal amount = 100_000m, bool group = false, Guid? coGuarantorId = null)
        {
            Guarantors = new FakeFundGuarantorRepository(FundRepo);

            Fund = group
                ? Fund.Create(GuarantorId, "Family Group", FundType.Family, 1_000_000m, "GROUP123",
                    guarantorMode: FundGuarantorMode.Group)
                : Fund.Create(GuarantorId, "Aunties Fund", FundType.Family, 1_000_000m, "ABCD1234");
            FundRepo.Funds.Add(Fund);

            Loan = Loan.Request(Fund.Id, MemberId, amount, RepaymentFrequency.Monthly, 0m,
                LoanFundingSource.GuarantorCapital);

            if (group)
            {
                Guarantors.Add(FundGuarantor.CreateOriginator(Fund.Id, GuarantorId, 1_000_000m));
                var invite = FundGuarantor.Invite(
                    Fund.Id, coGuarantorId!.Value, "co@example.com", 0m, GuarantorId);
                Guarantors.Add(invite);
                invite.Accept();
                CoGuarantorId = coGuarantorId;

                Loan.FreezeApprovedTerms(GuarantorId, amount, RepaymentFrequency.Monthly, amount);
                Loan.CompleteUnanimousApproval();
                ApproveByAllGuarantors();
            }
            else
            {
                Loan.Approve(GuarantorId, amount, RepaymentFrequency.Monthly, amount);
            }

            LoanRepo.Loans.Add(Loan);
            RepayRepo.Loans.Add(Loan);

            Service = new ManualDisbursementService(
                ManualRepo,
                LoanRepo,
                FundRepo,
                EvidenceRepo,
                PaymentRepo,
                new FakeFundAccessService(FundRepo, Guarantors),
                Guarantors,
                CapitalRepo,
                new RepaymentService(RepayRepo, new FakeAuditLog()),
                new FakeAuditLog());
        }

        private void ApproveByAllGuarantors()
        {
            var originator = LoanApproval.Require(Loan.Id, GuarantorId);
            originator.Approve();
            var co = LoanApproval.Require(Loan.Id, CoGuarantorId!.Value);
            co.Approve();
            LoanRepo.AddApprovals(new[] { originator, co });
        }

        public CreateManualDisbursementRequest Request(decimal? amount = null) => new()
        {
            LoanId = Loan.Id,
            Amount = amount ?? Loan.ApprovedAmount!.Value,
            Method = ManualDisbursementMethod.BankTransfer,
            DestinationBankName = "GTBank",
            DestinationAccountLast4 = "6789",
            Reference = "FT123",
        };

        public async Task UploadProofAsync(Guid reportId) =>
            await new EvidenceService(EvidenceRepo, new FakeFileStorage(), new FakeAuditLog())
                .UploadAsync(GuarantorId, "Disbursement", reportId, "proof.png", "image/png", new byte[] { 1, 2, 3 });

        public async Task<ManualDisbursementDto> CreateAndSubmitAsync()
        {
            var report = await Service.CreateDraftAsync(GuarantorId, Request());
            await UploadProofAsync(report.Id);
            return await Service.SubmitAsync(GuarantorId, report.Id);
        }
    }

    [Fact]
    public async Task CreateDraft_creates_a_draft_for_the_approved_loan()
    {
        var h = new Harness();

        var report = await h.Service.CreateDraftAsync(GuarantorId, h.Request());

        report.Status.Should().Be(ManualDisbursementStatus.Draft);
        report.Amount.Should().Be(100_000m);
        report.FundName.Should().Be("Aunties Fund");
        report.HasEvidence.Should().BeFalse();
        h.ManualRepo.Items.Should().ContainSingle(r => r.LoanId == h.Loan.Id);
        // Reporting an offline payment must not change the loan or the ledger.
        h.Loan.Status.Should().Be(LoanStatus.Approved);
        h.CapitalRepo.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateDraft_rejects_amount_that_differs_from_approved_amount()
    {
        var h = new Harness();

        var act = () => h.Service.CreateDraftAsync(GuarantorId, h.Request(amount: 90_000m));

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*must equal the approved loan amount*");
    }

    [Fact]
    public async Task CreateDraft_rejects_when_a_live_report_already_exists()
    {
        var h = new Harness();
        await h.Service.CreateDraftAsync(GuarantorId, h.Request());

        var act = () => h.Service.CreateDraftAsync(GuarantorId, h.Request());

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*already in progress*");
        h.ManualRepo.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateDraft_rejects_when_a_paystack_disbursement_is_in_progress()
    {
        var h = new Harness();
        h.PaymentRepo.AddDisbursement(DisbursementTransaction.Initiate(
            h.Loan.Id, Guid.NewGuid(), 100_000m, "TRF_1", "key-1"));

        var act = () => h.Service.CreateDraftAsync(GuarantorId, h.Request());

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*Paystack disbursement*");
        h.ManualRepo.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateDraft_rejects_non_originator()
    {
        var h = new Harness();

        var act = () => h.Service.CreateDraftAsync(Guid.NewGuid(), h.Request());

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*permission*");
    }

    [Fact]
    public async Task Submit_requires_proof_of_payment()
    {
        var h = new Harness();
        var report = await h.Service.CreateDraftAsync(GuarantorId, h.Request());

        var act = () => h.Service.SubmitAsync(GuarantorId, report.Id);

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*proof of payment*");
        h.Loan.Status.Should().Be(LoanStatus.Approved);
    }

    [Fact]
    public async Task Submit_moves_loan_to_disbursement_pending_without_disbursing()
    {
        var h = new Harness();

        var report = await h.CreateAndSubmitAsync();

        report.Status.Should().Be(ManualDisbursementStatus.PendingMemberConfirmation);
        report.HasEvidence.Should().BeTrue();
        h.Loan.Status.Should().Be(LoanStatus.DisbursementPending);
        h.CapitalRepo.Transactions.Should().BeEmpty();
        h.RepayRepo.Schedules.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_by_member_disburses_loan_and_records_manual_capital()
    {
        var h = new Harness();
        var report = await h.CreateAndSubmitAsync();

        var result = await h.Service.ConfirmByMemberAsync(MemberId, report.Id, "Received");

        result.Status.Should().Be(ManualDisbursementStatus.Confirmed);
        h.Loan.Status.Should().Be(LoanStatus.Disbursed);

        var capital = h.CapitalRepo.Transactions.Should().ContainSingle().Subject;
        capital.Provider.Should().Be(CapitalTransaction.ManualProvider);
        capital.Status.Should().Be(CapitalTransactionStatus.Confirmed);
        capital.AmountGross.Should().Be(100_000m);
        capital.AmountNet.Should().Be(100_000m);
        capital.ProviderFee.Should().Be(0m);
        capital.GuarantorId.Should().Be(GuarantorId);

        // Disbursement creates the v1 repayment schedule.
        h.RepayRepo.Schedules.Should().ContainSingle(s => s.LoanId == h.Loan.Id && s.Version == 1);
    }

    [Fact]
    public async Task Confirm_by_non_owner_throws_and_does_not_disburse()
    {
        var h = new Harness();
        var report = await h.CreateAndSubmitAsync();

        var act = () => h.Service.ConfirmByMemberAsync(Guid.NewGuid(), report.Id);

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*Only the member*");
        h.Loan.Status.Should().Be(LoanStatus.DisbursementPending);
        h.CapitalRepo.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_is_idempotent_and_never_double_disburses()
    {
        var h = new Harness();
        var report = await h.CreateAndSubmitAsync();
        await h.Service.ConfirmByMemberAsync(MemberId, report.Id);

        var act = () => h.Service.ConfirmByMemberAsync(MemberId, report.Id);

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*awaiting your confirmation*");
        h.CapitalRepo.Transactions.Should().ContainSingle();
        h.RepayRepo.Schedules.Should().ContainSingle();
    }

    [Fact]
    public async Task Dispute_keeps_loan_pending_and_the_report_live()
    {
        var h = new Harness();
        var report = await h.CreateAndSubmitAsync();

        var result = await h.Service.DisputeByMemberAsync(MemberId, report.Id, "Not received");

        result.Status.Should().Be(ManualDisbursementStatus.Disputed);
        h.Loan.Status.Should().Be(LoanStatus.DisbursementPending);
        h.CapitalRepo.Transactions.Should().BeEmpty();

        // The disputed report still blocks a new offline report until resolved.
        var act = () => h.Service.CreateDraftAsync(GuarantorId, h.Request());
        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*already in progress*");
    }

    [Fact]
    public async Task Cancel_from_pending_reverts_loan_to_approved_and_clears_the_guard()
    {
        var h = new Harness();
        var report = await h.CreateAndSubmitAsync();

        var result = await h.Service.CancelAsync(GuarantorId, report.Id, "Member unresponsive");

        result.Status.Should().Be(ManualDisbursementStatus.Cancelled);
        h.Loan.Status.Should().Be(LoanStatus.Approved);
        h.CapitalRepo.Transactions.Should().BeEmpty();

        // After cancelling, the Guarantor can retry.
        var retry = await h.Service.CreateDraftAsync(GuarantorId, h.Request());
        retry.Status.Should().Be(ManualDisbursementStatus.Draft);
    }

    [Fact]
    public async Task GetForOwner_returns_null_for_another_member()
    {
        var h = new Harness();
        var report = await h.CreateAndSubmitAsync();

        (await h.Service.GetForOwnerAsync(report.Id, MemberId)).Should().NotBeNull();
        (await h.Service.GetForOwnerAsync(report.Id, Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task Group_fund_rejects_report_from_a_co_guarantor()
    {
        var coGuarantorId = Guid.NewGuid();
        var h = new Harness(group: true, coGuarantorId: coGuarantorId);

        var act = () => h.Service.CreateDraftAsync(coGuarantorId, h.Request());

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*Only the Fund Originator*");
    }

    [Fact]
    public async Task Group_fund_requires_every_guarantor_to_have_approved()
    {
        var coGuarantorId = Guid.NewGuid();
        var h = new Harness(group: true, coGuarantorId: coGuarantorId);

        // Replace the co-Guarantor's approval with a pending one.
        h.LoanRepo.Approvals.RemoveAll(a => a.GuarantorId == coGuarantorId);
        h.LoanRepo.AddApproval(LoanApproval.Require(h.Loan.Id, coGuarantorId));

        var act = () => h.Service.CreateDraftAsync(GuarantorId, h.Request());

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*Every active Guarantor must approve*");
    }

    [Fact]
    public async Task GetMine_hides_draft_reports_until_they_are_submitted()
    {
        var h = new Harness();
        var report = await h.Service.CreateDraftAsync(GuarantorId, h.Request());

        h.ManualRepo.Details.Add(new ManualDisbursementWithDetails
        {
            Report = h.ManualRepo.Items.Single(),
            MemberId = MemberId,
            MemberDisplayName = "Ada Obi",
            MemberEmail = "ada@example.com",
            FundId = h.Fund.Id,
            FundName = h.Fund.Name,
            GuarantorId = GuarantorId,
        });

        // A draft has not reached the member yet.
        (await h.Service.GetMineAsync(MemberId)).Should().BeEmpty();

        await h.UploadProofAsync(report.Id);
        await h.Service.SubmitAsync(GuarantorId, report.Id);

        (await h.Service.GetMineAsync(MemberId))
            .Should().ContainSingle(r => r.Id == report.Id);
    }
}
