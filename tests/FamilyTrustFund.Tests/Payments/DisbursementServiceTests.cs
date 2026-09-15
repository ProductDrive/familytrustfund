using FamilyTrustFund.Application.Payments;
using FamilyTrustFund.Application.Repayments;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Payments;
using FamilyTrustFund.Tests.Support;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Payments;

public class DisbursementServiceTests
{
    private static readonly Guid GuarantorId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    private class Harness
    {
        public FakeFundRepository Funds { get; } = new();
        public FakeLoanRepository Loans { get; } = new();
        public FakePaymentRepository Payments { get; } = new();
        public FakeCapitalFundingRepository CapitalFunding { get; } = new();
        public FakePaymentProvider Provider { get; } = new();
        public FakeAuditLog Audit { get; } = new();
        public FakeRepaymentRepository Repayments { get; } = new();
        public DisbursementService Service { get; }

        public Harness()
        {
            var registry = new FakePaymentProviderRegistry();
            registry.ByName[Provider.Name] = Provider;
            Service = new DisbursementService(
                Payments,
                registry,
                CapitalFunding,
                Loans,
                Funds,
                new RepaymentService(Repayments, Audit),
                Audit);
        }

        public Fund AddFamilyFund()
        {
            var fund = Fund.Create(GuarantorId, "Aunties Fund", FundType.Family, 1_000_000m, "ABCD1234");
            Funds.Funds.Add(fund);
            return fund;
        }

        public Loan AddApprovedLoan(Fund fund)
        {
            var loan = Loan.Request(fund.Id, MemberId, 100_000m, RepaymentFrequency.Monthly, 0m, LoanFundingSource.GuarantorCapital);
            loan.Approve(GuarantorId, 100_000m, RepaymentFrequency.Monthly, 100_000m);
            Loans.Loans.Add(loan);
            Repayments.Loans.Add(loan);
            return loan;
        }

        public async Task AddVerifiedRecipientAsync()
        {
            await Service.SaveRecipientAsync(MemberId, new SaveRecipientRequest
            {
                BankCode = "058",
                BankName = "GTBank",
                AccountNumber = "0123456789",
                AccountName = "Ada Obi",
            }, ct: CancellationToken.None);
            await Service.VerifyRecipientAsync(MemberId, CancellationToken.None);
        }
    }

    private static Harness NewHarness() => new();

    [Fact]
    public async Task SaveRecipient_creates_unverified_recipient_and_disables_previous()
    {
        var h = NewHarness();

        await h.Service.SaveRecipientAsync(MemberId, new SaveRecipientRequest
        {
            BankCode = "058",
            BankName = "GTBank",
            AccountNumber = "0123456789",
            AccountName = "Ada Obi",
        }, ct: CancellationToken.None);
        await h.Service.SaveRecipientAsync(MemberId, new SaveRecipientRequest
        {
            BankCode = "011",
            BankName = "FirstBank",
            AccountNumber = "9876543210",
            AccountName = "Ada Obi",
        }, ct: CancellationToken.None);

        h.Payments.Recipients.Should().HaveCount(2);
        h.Payments.Recipients[0].IsActive.Should().BeFalse();
        h.Payments.Recipients[1].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyRecipient_activates_recipient_with_masked_account()
    {
        var h = NewHarness();

        await h.Service.SaveRecipientAsync(MemberId, new SaveRecipientRequest
        {
            BankCode = "058",
            BankName = "GTBank",
            AccountNumber = "0123456789",
            AccountName = "Ada Obi",
        }, ct: CancellationToken.None);
        var dto = await h.Service.VerifyRecipientAsync(MemberId, CancellationToken.None);

        dto.IsActive.Should().BeTrue();
        dto.Status.Should().Be(PaymentRecipientStatus.Active);
        dto.AccountNumberMasked.Should().Be("•••• 6789");
        h.Payments.Recipients.Single().ProviderRecipientCode.Should().Be("RCP_abc123");
    }

    [Fact]
    public async Task VerifyRecipient_throws_when_no_recipient_saved()
    {
        var h = NewHarness();

        var act = () => h.Service.VerifyRecipientAsync(MemberId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidPaymentException>();
    }

    [Fact]
    public async Task GetRecipientForMember_returns_latest_unverified_recipient()
    {
        var h = NewHarness();

        await h.Service.SaveRecipientAsync(MemberId, new SaveRecipientRequest
        {
            BankCode = "058",
            BankName = "GTBank",
            AccountNumber = "0123456789",
            AccountName = "Ada Obi",
        }, ct: CancellationToken.None);

        var dto = await h.Service.GetRecipientForMemberAsync(MemberId, CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.IsActive.Should().BeFalse();
        dto.Status.Should().Be(PaymentRecipientStatus.Unverified);
        dto.BankName.Should().Be("GTBank");
    }

    [Fact]
    public async Task GetRecipientForMember_returns_null_when_none_saved()
    {
        var h = NewHarness();

        var dto = await h.Service.GetRecipientForMemberAsync(MemberId, CancellationToken.None);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task InitiateDisbursement_creates_pending_transaction_and_marks_loan_pending()
    {
        var h = NewHarness();
        var fund = h.AddFamilyFund();
        var loan = h.AddApprovedLoan(fund);
        await h.AddVerifiedRecipientAsync();

        var dto = await h.Service.InitiateDisbursementAsync(GuarantorId, new InitiateDisbursementRequest { LoanId = loan.Id }, "guarantor@example.com", null, CancellationToken.None);

        dto.Status.Should().Be(DisbursementStatus.Pending);
        dto.Amount.Should().Be(100_000m);
        dto.ProviderReference.Should().Be("COL_chk789");
        dto.AuthorizationUrl.Should().Be("https://paystack.test/checkout");
        dto.GrossAmount.Should().Be(101_625m);
        dto.EstimatedFee.Should().Be(1_624.38m);
        loan.Status.Should().Be(LoanStatus.DisbursementPending);
        h.Payments.Transactions.Should().ContainSingle();

        // The Guarantor's per-loan capital payment must exist and be tied to this loan.
        var capital = h.CapitalFunding.Transactions.Should().ContainSingle().Subject;
        capital.ProviderReference.Should().Be("COL_chk789");
        capital.FundId.Should().Be(fund.Id);
        capital.LoanId.Should().Be(loan.Id);
        capital.GuarantorId.Should().Be(GuarantorId);
        capital.AmountGross.Should().Be(101_625m);
        capital.ProviderFee.Should().Be(1_624.38m);
        capital.AmountNet.Should().Be(100_000.62m);
        capital.Status.Should().Be(CapitalTransactionStatus.PendingConfirmation);
        h.Audit.Events.Should().Contain(e => e.Action == "Disbursement.Initiated");
    }

    [Fact]
    public async Task InitiateDisbursement_requires_verified_recipient()
    {
        var h = NewHarness();
        var fund = h.AddFamilyFund();
        var loan = h.AddApprovedLoan(fund);

        var act = () => h.Service.InitiateDisbursementAsync(GuarantorId, new InitiateDisbursementRequest { LoanId = loan.Id }, "guarantor@example.com", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*verified bank recipient*");
    }

    [Fact]
    public async Task InitiateDisbursement_non_owner_guarantor_throws()
    {
        var h = NewHarness();
        var fund = h.AddFamilyFund();
        var loan = h.AddApprovedLoan(fund);
        await h.AddVerifiedRecipientAsync();

        var act = () => h.Service.InitiateDisbursementAsync(Guid.NewGuid(), new InitiateDisbursementRequest { LoanId = loan.Id }, "guarantor@example.com", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*permission to disburse*");
    }

    [Fact]
    public async Task InitiateDisbursement_requires_settlement_subaccount()
    {
        var h = NewHarness();
        var fund = h.AddFamilyFund();
        var loan = h.AddApprovedLoan(fund);

        // A verified recipient without a settlement subaccount cannot be paid.
        var recipient = PaymentRecipient.Create(MemberId, "Paystack", "058", "GTBank", "0123456789", "Ada Obi");
        recipient.VerifyAndActivate("RCP_abc123");
        h.Payments.Recipients.Add(recipient);

        var act = () => h.Service.InitiateDisbursementAsync(GuarantorId, new InitiateDisbursementRequest { LoanId = loan.Id }, "guarantor@example.com", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*settlement subaccount*");
    }

    [Fact]
    public async Task InitiateDisbursement_rejects_loan_not_approved()
    {
        var h = NewHarness();
        var fund = h.AddFamilyFund();
        var loan = Loan.Request(fund.Id, MemberId, 100_000m, RepaymentFrequency.Monthly, 0m, LoanFundingSource.GuarantorCapital);
        h.Loans.Loans.Add(loan);
        await h.AddVerifiedRecipientAsync();

        var act = () => h.Service.InitiateDisbursementAsync(GuarantorId, new InitiateDisbursementRequest { LoanId = loan.Id }, "guarantor@example.com", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*approved loans*");
    }

    [Fact]
    public async Task InitiateDisbursement_prevents_double_initiation()
    {
        var h = NewHarness();
        var fund = h.AddFamilyFund();
        var loan = h.AddApprovedLoan(fund);
        await h.AddVerifiedRecipientAsync();

        await h.Service.InitiateDisbursementAsync(GuarantorId, new InitiateDisbursementRequest { LoanId = loan.Id }, "guarantor@example.com", null, CancellationToken.None);
        var act = () => h.Service.InitiateDisbursementAsync(GuarantorId, new InitiateDisbursementRequest { LoanId = loan.Id }, "guarantor@example.com", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*already been initiated*");
    }

    [Fact]
    public async Task InitiateDisbursement_can_be_retried_after_failed_payment()
    {
        var h = NewHarness();
        var fund = h.AddFamilyFund();
        var loan = h.AddApprovedLoan(fund);
        await h.AddVerifiedRecipientAsync();

        await h.Service.InitiateDisbursementAsync(GuarantorId, new InitiateDisbursementRequest { LoanId = loan.Id }, "guarantor@example.com", null, CancellationToken.None);
        var failed = await h.Service.ProcessProviderWebhookAsync(
            "Paystack", "evt-1", "COL_chk789", DisbursementStatus.Failed, "Insufficient balance", CancellationToken.None);

        failed!.Status.Should().Be(DisbursementStatus.Failed);
        failed.FailureReason.Should().Be("Insufficient balance");
        loan.Status.Should().Be(LoanStatus.DisbursementPending);

        var retried = await h.Service.InitiateDisbursementAsync(GuarantorId, new InitiateDisbursementRequest { LoanId = loan.Id }, "guarantor@example.com", null, CancellationToken.None);

        retried.Status.Should().Be(DisbursementStatus.Pending);
        retried.ProviderReference.Should().Be("COL_chk789");
        loan.Status.Should().Be(LoanStatus.DisbursementPending);

        // The failed settlement record is reused for the retry, not duplicated.
        h.Payments.Transactions.Should().ContainSingle();
    }

    [Fact]
    public async Task Webhook_success_marks_loan_disbursed_idempotently()
    {
        var h = NewHarness();
        var fund = h.AddFamilyFund();
        var loan = h.AddApprovedLoan(fund);
        await h.AddVerifiedRecipientAsync();
        await h.Service.InitiateDisbursementAsync(GuarantorId, new InitiateDisbursementRequest { LoanId = loan.Id }, "guarantor@example.com", null, CancellationToken.None);

        var processed = await h.Service.ProcessProviderWebhookAsync(
            "Paystack", "evt-1", "COL_chk789", DisbursementStatus.Successful, ct: CancellationToken.None);

        processed!.Status.Should().Be(DisbursementStatus.Successful);
        loan.Status.Should().Be(LoanStatus.Disbursed);
        h.Audit.Events.Should().Contain(e => e.Action == "Disbursement.Succeeded");

        // A v1 repayment schedule must exist as soon as the loan is disbursed so
        // the member can repay immediately.
        h.Repayments.Schedules.Should().ContainSingle(s => s.LoanId == loan.Id);
        h.Repayments.Schedules.Single(s => s.LoanId == loan.Id).Items.Should().NotBeEmpty();

        // A retry of the same event must not alter the loan again.
        var duplicate = await h.Service.ProcessProviderWebhookAsync(
            "Paystack", "evt-1", "COL_chk789", DisbursementStatus.Successful, ct: CancellationToken.None);
        duplicate.Should().BeNull();
        loan.Status.Should().Be(LoanStatus.Disbursed);
        h.Repayments.Schedules.Should().ContainSingle(s => s.LoanId == loan.Id);
    }

    [Fact]
    public async Task Webhook_failure_leaves_loan_in_pending_state()
    {
        var h = NewHarness();
        var fund = h.AddFamilyFund();
        var loan = h.AddApprovedLoan(fund);
        await h.AddVerifiedRecipientAsync();
        await h.Service.InitiateDisbursementAsync(GuarantorId, new InitiateDisbursementRequest { LoanId = loan.Id }, "guarantor@example.com", null, CancellationToken.None);

        var processed = await h.Service.ProcessProviderWebhookAsync(
            "Paystack", "evt-2", "COL_chk789", DisbursementStatus.Failed, "Insufficient balance", CancellationToken.None);

        processed!.Status.Should().Be(DisbursementStatus.Failed);
        processed.FailureReason.Should().Be("Insufficient balance");
        loan.Status.Should().Be(LoanStatus.DisbursementPending);
    }

    [Fact]
    public async Task Webhook_reversal_records_reversed_state()
    {
        var h = NewHarness();
        var fund = h.AddFamilyFund();
        var loan = h.AddApprovedLoan(fund);
        await h.AddVerifiedRecipientAsync();
        await h.Service.InitiateDisbursementAsync(GuarantorId, new InitiateDisbursementRequest { LoanId = loan.Id }, "guarantor@example.com", null, CancellationToken.None);

        await h.Service.ProcessProviderWebhookAsync(
            "Paystack", "evt-1", "COL_chk789", DisbursementStatus.Successful, ct: CancellationToken.None);
        var reversed = await h.Service.ProcessProviderWebhookAsync(
            "Paystack", "evt-2", "COL_chk789", DisbursementStatus.Reversed, "Reversed by bank", CancellationToken.None);

        reversed!.Status.Should().Be(DisbursementStatus.Reversed);
        reversed.FailureReason.Should().Be("Reversed by bank");
        loan.Status.Should().Be(LoanStatus.Disbursed); // Reversal after success keeps audit records; loan marked disbursed already.
    }

    [Fact]
    public async Task Charge_success_webhook_finalises_disbursement_and_marks_loan_disbursed()
    {
        var h = NewHarness();
        var fund = h.AddFamilyFund();
        var loan = h.AddApprovedLoan(fund);
        await h.AddVerifiedRecipientAsync();
        await h.Service.InitiateDisbursementAsync(GuarantorId, new InitiateDisbursementRequest { LoanId = loan.Id }, "guarantor@example.com", null, CancellationToken.None);

        var finalised = await h.Service.FinaliseDisbursementForChargeAsync(
            "charge-evt-1", "COL_chk789", true, "charge.success", CancellationToken.None);

        finalised.Should().NotBeNull();
        finalised!.Status.Should().Be(DisbursementStatus.Successful);
        loan.Status.Should().Be(LoanStatus.Disbursed);
        h.Repayments.Schedules.Should().ContainSingle(s => s.LoanId == loan.Id);

        var duplicate = await h.Service.FinaliseDisbursementForChargeAsync(
            "charge-evt-1", "COL_chk789", true, "charge.success", CancellationToken.None);
        duplicate.Should().BeNull();
        loan.Status.Should().Be(LoanStatus.Disbursed);
    }

    [Fact]
    public async Task Charge_failed_webhook_marks_disbursement_failed_and_loan_retryable()
    {
        var h = NewHarness();
        var fund = h.AddFamilyFund();
        var loan = h.AddApprovedLoan(fund);
        await h.AddVerifiedRecipientAsync();
        await h.Service.InitiateDisbursementAsync(GuarantorId, new InitiateDisbursementRequest { LoanId = loan.Id }, "guarantor@example.com", null, CancellationToken.None);

        var finalised = await h.Service.FinaliseDisbursementForChargeAsync(
            "charge-evt-2", "COL_chk789", false, "charge.failed", CancellationToken.None);

        finalised.Should().NotBeNull();
        finalised!.Status.Should().Be(DisbursementStatus.Failed);
        finalised.FailureReason.Should().Be("charge.failed");
        loan.Status.Should().Be(LoanStatus.DisbursementPending);
    }

    [Fact]
    public async Task Charge_webhook_without_linked_disbursement_returns_null()
    {
        var h = NewHarness();
        var fund = h.AddFamilyFund();
        h.CapitalFunding.Transactions.Add(CapitalTransaction.Create(
            fund.Id, Guid.NewGuid(), GuarantorId, "Paystack", 101_600m, "COL_standalone"));

        var finalised = await h.Service.FinaliseDisbursementForChargeAsync(
            "charge-evt-3", "COL_standalone", true, "charge.success", CancellationToken.None);

        finalised.Should().BeNull();
    }

    [Fact]
    public async Task Webhook_unknown_reference_throws()
    {
        var h = NewHarness();

        var act = () => h.Service.ProcessProviderWebhookAsync(
            "Paystack", "evt-1", "TRF_unknown", DisbursementStatus.Successful, ct: CancellationToken.None);

        await act.Should().ThrowAsync<InvalidPaymentException>();
    }
}
