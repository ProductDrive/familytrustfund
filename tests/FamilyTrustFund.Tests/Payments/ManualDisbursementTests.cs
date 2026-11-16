using FamilyTrustFund.Domain.Payments;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Payments;

/// <summary>
/// Domain rules for the offline/manual disbursement report (ADR-056): the
/// amount must equal the approved amount, the lifecycle is guarded, and only
/// the owning member can confirm or dispute.
/// </summary>
public class ManualDisbursementTests
{
    private static readonly Guid LoanId = Guid.NewGuid();
    private static readonly Guid FundId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid GuarantorId = Guid.NewGuid();

    private static ManualDisbursement Draft(decimal amount = 100_000m, decimal? approved = null) =>
        ManualDisbursement.CreateDraft(
            LoanId,
            FundId,
            MemberId,
            GuarantorId,
            amount,
            approved ?? amount,
            ManualDisbursementMethod.BankTransfer,
            "GTBank",
            "6789",
            "FT123",
            "Offline payment");

    [Fact]
    public void CreateDraft_starts_as_draft_and_is_live()
    {
        var report = Draft();

        report.Status.Should().Be(ManualDisbursementStatus.Draft);
        report.IsLive.Should().BeTrue();
        report.Amount.Should().Be(100_000m);
        report.DestinationAccountLast4.Should().Be("6789");
        report.Currency.Should().Be("NGN");
    }

    [Fact]
    public void CreateDraft_rejects_amount_that_does_not_match_approved_amount()
    {
        var act = () => Draft(amount: 90_000m, approved: 100_000m);

        act.Should().Throw<InvalidPaymentException>()
            .WithMessage("*must equal the approved loan amount*");
    }

    [Fact]
    public void CreateDraft_accepts_amount_that_matches_after_rounding()
    {
        var report = Draft(amount: 100_000.004m, approved: 100_000m);

        report.Amount.Should().Be(100_000m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateDraft_rejects_non_positive_amount(decimal amount)
    {
        var act = () => Draft(amount);
        act.Should().Throw<InvalidPaymentException>();
    }

    [Fact]
    public void CreateDraft_rejects_invalid_method()
    {
        var act = () => ManualDisbursement.CreateDraft(
            LoanId, FundId, MemberId, GuarantorId, 100m, 100m, (ManualDisbursementMethod)99);
        act.Should().Throw<InvalidPaymentException>();
    }

    [Fact]
    public void Submit_moves_draft_to_pending_member_confirmation()
    {
        var report = Draft();

        report.Submit();

        report.Status.Should().Be(ManualDisbursementStatus.PendingMemberConfirmation);
        report.SubmittedAtUtc.Should().NotBeNull();
        report.IsLive.Should().BeTrue();
    }

    [Fact]
    public void Submit_is_rejected_when_not_a_draft()
    {
        var report = Draft();
        report.Submit();

        var act = () => report.Submit();

        act.Should().Throw<InvalidPaymentException>()
            .WithMessage("*draft*");
    }

    [Fact]
    public void Confirm_by_owner_marks_confirmed_and_is_no_longer_live()
    {
        var report = Draft();
        report.Submit();

        report.Confirm(MemberId, "Received, thank you");

        report.Status.Should().Be(ManualDisbursementStatus.Confirmed);
        report.ConfirmedAtUtc.Should().NotBeNull();
        report.MemberNote.Should().Be("Received, thank you");
        report.IsLive.Should().BeFalse();
    }

    [Fact]
    public void Confirm_by_non_owner_throws()
    {
        var report = Draft();
        report.Submit();

        var act = () => report.Confirm(Guid.NewGuid());

        act.Should().Throw<InvalidPaymentException>()
            .WithMessage("*Only the member*");
    }

    [Fact]
    public void Confirm_is_idempotent_second_confirm_throws()
    {
        var report = Draft();
        report.Submit();
        report.Confirm(MemberId);

        var act = () => report.Confirm(MemberId);

        act.Should().Throw<InvalidPaymentException>()
            .WithMessage("*awaiting your confirmation*");
    }

    [Fact]
    public void Dispute_by_owner_stays_live()
    {
        var report = Draft();
        report.Submit();

        report.Dispute(MemberId, "Money not received");

        report.Status.Should().Be(ManualDisbursementStatus.Disputed);
        report.MemberNote.Should().Be("Money not received");
        report.DisputedAtUtc.Should().NotBeNull();
        report.IsLive.Should().BeTrue();
    }

    [Fact]
    public void Dispute_requires_a_reason()
    {
        var report = Draft();
        report.Submit();

        var act = () => report.Dispute(MemberId, "  ");

        act.Should().Throw<InvalidPaymentException>()
            .WithMessage("*reason is required*");
    }

    [Fact]
    public void Cancel_from_pending_member_confirmation_clears_live_flag()
    {
        var report = Draft();
        report.Submit();

        report.Cancel("Member did not respond");

        report.Status.Should().Be(ManualDisbursementStatus.Cancelled);
        report.CancelReason.Should().Be("Member did not respond");
        report.CancelledAtUtc.Should().NotBeNull();
        report.IsLive.Should().BeFalse();
    }

    [Fact]
    public void Cancel_from_disputed_is_allowed()
    {
        var report = Draft();
        report.Submit();
        report.Dispute(MemberId, "Not received");

        report.Cancel("Retrying");

        report.Status.Should().Be(ManualDisbursementStatus.Cancelled);
    }

    [Fact]
    public void Cancel_of_confirmed_report_is_rejected()
    {
        var report = Draft();
        report.Submit();
        report.Confirm(MemberId);

        var act = () => report.Cancel("Too late");

        act.Should().Throw<InvalidPaymentException>()
            .WithMessage("*cannot be cancelled*");
    }
}
