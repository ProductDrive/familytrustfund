using FamilyTrustFund.Domain.Payments;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Payments;

public class DisbursementTransactionTests
{
    private static readonly Guid LoanId = Guid.NewGuid();
    private static readonly Guid RecipientId = Guid.NewGuid();

    [Fact]
    public void Initiate_creates_pending_transaction()
    {
        var t = DisbursementTransaction.Initiate(LoanId, RecipientId, 100_000m, "TRF_1", "key-1");

        t.LoanId.Should().Be(LoanId);
        t.RecipientId.Should().Be(RecipientId);
        t.Amount.Should().Be(100_000m);
        t.Currency.Should().Be("NGN");
        t.ProviderReference.Should().Be("TRF_1");
        t.IdempotencyKey.Should().Be("key-1");
        t.Status.Should().Be(DisbursementStatus.Pending);
        t.CompletedAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Initiate_rejects_non_positive_amount(decimal amount)
    {
        var act = () => DisbursementTransaction.Initiate(LoanId, RecipientId, amount, "TRF_1", "key-1");
        act.Should().Throw<InvalidPaymentException>();
    }

    [Fact]
    public void Initiate_rejects_missing_provider_reference()
    {
        var act = () => DisbursementTransaction.Initiate(LoanId, RecipientId, 100m, "  ", "key-1");
        act.Should().Throw<InvalidPaymentException>();
    }

    [Fact]
    public void Initiate_rejects_missing_idempotency_key()
    {
        var act = () => DisbursementTransaction.Initiate(LoanId, RecipientId, 100m, "TRF_1", " ");
        act.Should().Throw<InvalidPaymentException>();
    }

    [Fact]
    public void ApplyProviderEvent_marks_successful_and_records_event()
    {
        var t = DisbursementTransaction.Initiate(LoanId, RecipientId, 100m, "TRF_1", "key-1");

        var applied = t.ApplyProviderEvent("evt-1", DisbursementStatus.Successful);

        applied.Should().BeTrue();
        t.Status.Should().Be(DisbursementStatus.Successful);
        t.LastProcessedEventId.Should().Be("evt-1");
        t.CompletedAtUtc.Should().NotBeNull();
        t.FailureReason.Should().BeNull();
    }

    [Fact]
    public void ApplyProviderEvent_duplicate_event_is_ignored()
    {
        var t = DisbursementTransaction.Initiate(LoanId, RecipientId, 100m, "TRF_1", "key-1");
        t.ApplyProviderEvent("evt-1", DisbursementStatus.Successful);

        var applied = t.ApplyProviderEvent("evt-1", DisbursementStatus.Failed);

        applied.Should().BeFalse();
        t.Status.Should().Be(DisbursementStatus.Successful);
    }

    [Fact]
    public void ApplyProviderEvent_failure_records_reason()
    {
        var t = DisbursementTransaction.Initiate(LoanId, RecipientId, 100m, "TRF_1", "key-1");

        t.ApplyProviderEvent("evt-1", DisbursementStatus.Failed, "Insufficient balance");

        t.Status.Should().Be(DisbursementStatus.Failed);
        t.FailureReason.Should().Be("Insufficient balance");
        t.CompletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void ApplyProviderEvent_reversed_records_state()
    {
        var t = DisbursementTransaction.Initiate(LoanId, RecipientId, 100m, "TRF_1", "key-1");
        t.ApplyProviderEvent("evt-1", DisbursementStatus.Successful);

        t.ApplyProviderEvent("evt-2", DisbursementStatus.Reversed, "Reversed by bank");

        t.Status.Should().Be(DisbursementStatus.Reversed);
        t.FailureReason.Should().Be("Reversed by bank");
    }
}
