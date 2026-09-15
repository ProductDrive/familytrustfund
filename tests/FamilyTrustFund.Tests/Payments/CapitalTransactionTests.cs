using FamilyTrustFund.Domain.Payments;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Payments;

public class CapitalTransactionTests
{
    private static readonly Guid FundId = Guid.NewGuid();
    private static readonly Guid LoanId = Guid.NewGuid();
    private static readonly Guid GuarantorId = Guid.NewGuid();

    [Fact]
    public void Create_records_gross_fee_and_net_estimate()
    {
        var t = CapitalTransaction.Create(FundId, LoanId, GuarantorId, "Paystack", 101_600m, "CAP-1");

        t.Status.Should().Be(CapitalTransactionStatus.PendingConfirmation);
        t.AmountGross.Should().Be(101_600m);
        t.ProviderFee.Should().BeGreaterThan(0);
        t.AmountNet.Should().Be(t.AmountGross - t.ProviderFee);
    }

    [Fact]
    public void Create_rejects_invalid_amounts()
    {
        var act = () => CapitalTransaction.Create(FundId, LoanId, GuarantorId, "Paystack", 0m, "CAP-1");
        act.Should().Throw<InvalidPaymentException>();

        var act2 = () => CapitalTransaction.Create(Guid.Empty, LoanId, GuarantorId, "Paystack", 100m, "CAP-1");
        act2.Should().Throw<InvalidPaymentException>();
    }

    [Fact]
    public void ApplyProviderEvent_confirms_with_authoritative_fee_and_net()
    {
        var t = CapitalTransaction.Create(FundId, LoanId, GuarantorId, "Paystack", 101_600m, "CAP-1");

        var applied = t.ApplyProviderEvent("evt-1", true, providerFee: 1_600m);

        applied.Should().BeTrue();
        t.Status.Should().Be(CapitalTransactionStatus.Confirmed);
        t.ProviderFee.Should().Be(1_600m);
        t.AmountNet.Should().Be(100_000m);
        t.CompletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void ApplyProviderEvent_failure_marks_failed()
    {
        var t = CapitalTransaction.Create(FundId, LoanId, GuarantorId, "Paystack", 101_600m, "CAP-1");

        var applied = t.ApplyProviderEvent("evt-1", false, providerFee: null, detail: "Card declined");

        applied.Should().BeTrue();
        t.Status.Should().Be(CapitalTransactionStatus.Failed);
        t.FailureReason.Should().Be("Card declined");
        t.AmountNet.Should().BeNull();
    }

    [Fact]
    public void ApplyProviderEvent_is_idempotent_for_duplicate_event()
    {
        var t = CapitalTransaction.Create(FundId, LoanId, GuarantorId, "Paystack", 101_600m, "CAP-1");

        t.ApplyProviderEvent("evt-1", true, 1_600m).Should().BeTrue();
        t.ApplyProviderEvent("evt-1", true, 1_600m).Should().BeFalse();
        t.LastProcessedEventId.Should().Be("evt-1");
        t.Status.Should().Be(CapitalTransactionStatus.Confirmed);
    }

    [Fact]
    public void Already_confirmed_payment_ignores_later_events()
    {
        var t = CapitalTransaction.Create(FundId, LoanId, GuarantorId, "Paystack", 101_600m, "CAP-1");
        t.ApplyProviderEvent("evt-1", true, 1_600m);

        t.ApplyProviderEvent("evt-2", false, null, "late failure").Should().BeFalse();
        t.Status.Should().Be(CapitalTransactionStatus.Confirmed);
    }

    [Theory]
    [InlineData(100_000, 1_600)]   // 1.5% + 100 = 1500 + 100
    [InlineData(2_000, 30)]         // below waiver threshold: 1.5% only
    [InlineData(300_000, 2_000)]    // capped at 2,000
    public void ComputeEstimatedFee_follows_ngn_schedule(decimal gross, decimal expectedFee) =>
        CapitalTransaction.ComputeEstimatedFee(gross).Should().Be(expectedFee);

    [Theory]
    [InlineData(100_000, 101_624.37)]
    [InlineData(10_000, 10_253.81)]
    [InlineData(2_000, 2_030.46)]    // below waiver threshold: 1.5% only
    [InlineData(200_000, 202_000)]   // fee capped at 2,000
    public void ComputeGrossForNet_nets_exact_amount_after_fee(decimal net, decimal expectedGross)
    {
        var gross = CapitalTransaction.ComputeGrossForNet(net);

        gross.Should().Be(expectedGross);
        (gross - CapitalTransaction.ComputeEstimatedFee(gross)).Should().Be(net);
    }

    [Fact]
    public void ComputeGrossForNet_rejects_non_positive_net()
    {
        var act = () => CapitalTransaction.ComputeGrossForNet(0m);
        act.Should().Throw<InvalidPaymentException>();
    }
}