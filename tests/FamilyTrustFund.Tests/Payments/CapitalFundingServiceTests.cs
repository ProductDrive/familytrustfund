using FamilyTrustFund.Application.Payments;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Payments;
using FamilyTrustFund.Tests.Support;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Payments;

/// <summary>
/// Unit tests for the Guarantor's per-loan capital funding payments (ADR-044).
/// The Guarantor does not pre-fund a pool: they pay at disbursement for each
/// loan. These tests exercise the server-authoritative estimate, provider
/// verification and idempotent webhook processing.
/// </summary>
public class CapitalFundingServiceTests
{
    private static readonly Guid GuarantorId = Guid.NewGuid();
    private static readonly Guid LoanId = Guid.NewGuid();

    private class Harness
    {
        public FakeFundRepository Funds { get; } = new();
        public FakeCapitalFundingRepository CapitalFunding { get; } = new();
        public FakePaymentProvider Provider { get; } = new();
        public FakeAuditLog Audit { get; } = new();
        public CapitalFundingService Service { get; }

        private int _fundSeed;

        public Harness()
        {
            var registry = new FakePaymentProviderRegistry();
            registry.ByName[Provider.Name] = Provider;
            Service = new CapitalFundingService(Funds, CapitalFunding, registry, Audit);
        }

        public Fund AddFund()
        {
            var fund = Fund.Create(
                GuarantorId,
                "Capital Payments Fund",
                FundType.External,
                committedCapital: 1_000_000m,
                joinCode: $"CAP-{_fundSeed++:D4}",
                interestRate: 5m);
            Funds.Funds.Add(fund);
            return fund;
        }

        public CapitalTransaction AddTransaction(Guid fundId, decimal gross = 101_600m, string? reference = null)
        {
            var tx = CapitalTransaction.Create(
                fundId, LoanId, GuarantorId, "Paystack", gross, reference ?? $"CAP-{Guid.NewGuid():N}");
            CapitalFunding.Transactions.Add(tx);
            return tx;
        }
    }

    private static Harness NewHarness() => new();

    [Fact]
    public async Task Estimate_returns_gross_with_estimated_fee()
    {
        var h = NewHarness();
        var fund = h.AddFund();
        h.Provider.EstimatedFee = 1_700m;

        var dto = await h.Service.EstimateAsync(
            GuarantorId,
            fund.Id,
            new CapitalFundingEstimateRequest { Amount = 100_000m },
            CancellationToken.None);

        dto.Amount.Should().Be(100_000m);
        dto.EstimatedFee.Should().Be(1_700m);
        dto.GrossAmount.Should().Be(101_700m);
    }

    [Fact]
    public async Task Estimate_non_owner_throws()
    {
        var h = NewHarness();
        var fund = h.AddFund();

        var act = () => h.Service.EstimateAsync(
            Guid.NewGuid(),
            fund.Id,
            new CapitalFundingEstimateRequest { Amount = 100_000m },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*permission to fund*");
    }

    [Fact]
    public async Task Estimate_rejects_non_positive_amount()
    {
        var h = NewHarness();
        var fund = h.AddFund();

        var act = () => h.Service.EstimateAsync(
            GuarantorId,
            fund.Id,
            new CapitalFundingEstimateRequest { Amount = 0m },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*greater than zero*");
    }

    [Fact]
    public async Task Verify_unpaid_payment_stays_pending()
    {
        var h = NewHarness();
        var fund = h.AddFund();
        var tx = h.AddTransaction(fund.Id);
        h.Provider.VerifyPaid = false;

        var dto = await h.Service.VerifyAsync(GuarantorId, fund.Id, tx.ProviderReference, CancellationToken.None);

        dto.Status.Should().Be(CapitalTransactionStatus.PendingConfirmation);
        h.Audit.Events.Should().NotContain(e => e.Action == "CapitalPayment.Confirmed");
    }

    [Fact]
    public async Task Verify_confirming_records_authoritative_fee_and_net()
    {
        var h = NewHarness();
        var fund = h.AddFund();
        h.Provider.VerifyFee = 1_500m;
        var tx = h.AddTransaction(fund.Id);

        var dto = await h.Service.VerifyAsync(GuarantorId, fund.Id, tx.ProviderReference, CancellationToken.None);

        dto.Status.Should().Be(CapitalTransactionStatus.Confirmed);
        dto.ProviderFee.Should().Be(1_500m);
        dto.AmountNet.Should().Be(dto.AmountGross - 1_500m);
        h.Audit.Events.Should().Contain(e => e.Action == "CapitalPayment.Confirmed");
    }

    [Fact]
    public async Task Verify_is_idempotent()
    {
        var h = NewHarness();
        var fund = h.AddFund();
        var tx = h.AddTransaction(fund.Id);

        var first = await h.Service.VerifyAsync(GuarantorId, fund.Id, tx.ProviderReference, CancellationToken.None);
        var second = await h.Service.VerifyAsync(GuarantorId, fund.Id, tx.ProviderReference, CancellationToken.None);

        first.Status.Should().Be(CapitalTransactionStatus.Confirmed);
        second.Status.Should().Be(CapitalTransactionStatus.Confirmed);
        second.AmountNet.Should().Be(first.AmountNet);
        h.Audit.Events.Should().ContainSingle(e => e.Action == "CapitalPayment.Confirmed");
    }

    [Fact]
    public async Task Verify_non_owner_throws()
    {
        var h = NewHarness();
        var fund = h.AddFund();
        var tx = h.AddTransaction(fund.Id);

        var act = () => h.Service.VerifyAsync(Guid.NewGuid(), fund.Id, tx.ProviderReference, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*permission to access*");
    }

    [Fact]
    public async Task Webhook_charge_success_confirms_idempotently()
    {
        var h = NewHarness();
        var fund = h.AddFund();
        var tx = h.AddTransaction(fund.Id);

        var processed = await h.Service.ProcessChargeWebhookAsync(
            "Paystack", "evt-1", tx.ProviderReference, true, 1_600m, null, CancellationToken.None);

        processed!.Status.Should().Be(CapitalTransactionStatus.Confirmed);
        processed.AmountNet.Should().Be(processed.AmountGross - 1_600m);
        h.Audit.Events.Should().Contain(e => e.Action == "CapitalPayment.Confirmed");

        var duplicate = await h.Service.ProcessChargeWebhookAsync(
            "Paystack", "evt-1", tx.ProviderReference, true, 1_600m, null, CancellationToken.None);

        duplicate.Should().BeNull();
        h.Audit.Events.Should().ContainSingle(e => e.Action == "CapitalPayment.Confirmed");
    }

    [Fact]
    public async Task Webhook_charge_failed_marks_failed()
    {
        var h = NewHarness();
        var fund = h.AddFund();
        var tx = h.AddTransaction(fund.Id);

        var processed = await h.Service.ProcessChargeWebhookAsync(
            "Paystack", "evt-2", tx.ProviderReference, false, null, "charge.failed", CancellationToken.None);

        processed!.Status.Should().Be(CapitalTransactionStatus.Failed);
        processed.AmountNet.Should().BeNull();
        processed.FailureReason.Should().Be("charge.failed");
    }

    [Fact]
    public async Task Webhook_unknown_reference_throws()
    {
        var h = NewHarness();

        var act = () => h.Service.ProcessChargeWebhookAsync(
            "Paystack", "evt-3", "unknown-ref", true, null, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*Unknown provider collection reference*");
    }

    [Fact]
    public async Task Summary_is_confirmed_net_and_transaction_count()
    {
        var h = NewHarness();
        var fund = h.AddFund();
        h.AddTransaction(fund.Id);
        h.AddTransaction(fund.Id);
        h.CapitalFunding.ConfirmedNetByFund[fund.Id] = 500_000m;

        var summary = await h.Service.GetSummaryAsync(GuarantorId, fund.Id, CancellationToken.None);

        summary.TotalFunded.Should().Be(500_000m);
        summary.TransactionCount.Should().Be(2);
    }

    [Fact]
    public async Task GetTransactions_returns_owned_fund_payments_paged()
    {
        var h = NewHarness();
        var fund = h.AddFund();
        for (var i = 0; i < 5; i++)
        {
            h.AddTransaction(fund.Id, 10_000m * (i + 1));
        }

        var otherFund = h.AddFund();
        h.AddTransaction(otherFund.Id, 99_000m);

        var page = await h.Service.GetTransactionsAsync(GuarantorId, fund.Id, 1, 3, CancellationToken.None);

        page.TotalCount.Should().Be(5);
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(3);
        page.Items.Should().HaveCount(3);
        page.Items.Should().OnlyContain(i =>
            i.FundId == fund.Id && i.AmountGross >= 10_000m && i.AmountGross <= 50_000m);
    }

    [Fact]
    public async Task GetTransactions_non_owner_throws()
    {
        var h = NewHarness();
        var fund = h.AddFund();

        var act = () => h.Service.GetTransactionsAsync(Guid.NewGuid(), fund.Id, 1, 10, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*permission to fund*");
    }
}