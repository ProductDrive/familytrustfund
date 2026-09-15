using FamilyTrustFund.Domain.Contributions;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Contributions;

public class FundContributionTests
{
    private static readonly Guid FundId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    [Fact]
    public void Report_creates_pending_contribution_with_correct_values()
    {
        var contribution = FundContribution.Report(FundId, MemberId, 100_000m, "FT123", "Monthly savings");

        contribution.FundId.Should().Be(FundId);
        contribution.MemberId.Should().Be(MemberId);
        contribution.Amount.Should().Be(100_000m);
        contribution.Status.Should().Be(ContributionStatus.PendingConfirmation);
        contribution.Reference.Should().Be("FT123");
        contribution.Note.Should().Be("Monthly savings");
        contribution.ConfirmedAtUtc.Should().BeNull();
        contribution.IsConfirmed.Should().BeFalse();
    }

    [Fact]
    public void Report_empty_fund_throws()
    {
        var act = () => FundContribution.Report(Guid.Empty, MemberId, 100m);
        act.Should().Throw<InvalidContributionException>();
    }

    [Fact]
    public void Report_zero_amount_throws()
    {
        var act = () => FundContribution.Report(FundId, MemberId, 0m);
        act.Should().Throw<InvalidContributionException>();
    }

    [Fact]
    public void Report_negative_amount_throws()
    {
        var act = () => FundContribution.Report(FundId, MemberId, -100m);
        act.Should().Throw<InvalidContributionException>();
    }

    [Fact]
    public void Report_overlong_note_throws()
    {
        var act = () => FundContribution.Report(FundId, MemberId, 100m, note: new string('x', 1001));
        act.Should().Throw<InvalidContributionException>();
    }

    [Fact]
    public void Confirm_marks_contribution_confirmed()
    {
        var contribution = FundContribution.Report(FundId, MemberId, 100m);

        contribution.Confirm();

        contribution.Status.Should().Be(ContributionStatus.Confirmed);
        contribution.ConfirmedAtUtc.Should().NotBeNull();
        contribution.IsConfirmed.Should().BeTrue();
    }

    [Fact]
    public void Confirm_with_note_stores_confirmation_note_trimmed()
    {
        var contribution = FundContribution.Report(FundId, MemberId, 100m);

        contribution.Confirm("  Verified with member  ");

        contribution.ConfirmationNote.Should().Be("Verified with member");
    }

    [Fact]
    public void Confirm_blank_note_is_null()
    {
        var contribution = FundContribution.Report(FundId, MemberId, 100m);

        contribution.Confirm("   ");

        contribution.ConfirmationNote.Should().BeNull();
    }

    [Fact]
    public void Confirm_overlong_confirmation_note_throws()
    {
        var contribution = FundContribution.Report(FundId, MemberId, 100m);

        var act = () => contribution.Confirm(new string('x', 1001));

        act.Should().Throw<InvalidContributionException>();
    }

    [Fact]
    public void Confirm_null_or_whitespace_fields_are_normalized()
    {
        var contribution = FundContribution.Report(FundId, MemberId, 100m, "  ", "   ");
        contribution.Reference.Should().BeNull();
        contribution.Note.Should().BeNull();
    }

    [Fact]
    public void Confirm_already_confirmed_throws()
    {
        var contribution = FundContribution.Report(FundId, MemberId, 100m);
        contribution.Confirm();

        var act = () => contribution.Confirm();
        act.Should().Throw<InvalidContributionException>();
    }

    [Fact]
    public void Confirm_rejected_throws()
    {
        var contribution = FundContribution.Report(FundId, MemberId, 100m);
        contribution.Reject("Duplicate");

        var act = () => contribution.Confirm();
        act.Should().Throw<InvalidContributionException>();
    }

    [Fact]
    public void Reject_marks_contribution_rejected()
    {
        var contribution = FundContribution.Report(FundId, MemberId, 100m);

        contribution.Reject("Duplicate report");

        contribution.Status.Should().Be(ContributionStatus.Rejected);
        contribution.RejectionReason.Should().Be("Duplicate report");
        contribution.RejectedAtUtc.Should().NotBeNull();
        contribution.IsConfirmed.Should().BeFalse();
    }

    [Fact]
    public void Reject_without_reason_is_null()
    {
        var contribution = FundContribution.Report(FundId, MemberId, 100m);
        contribution.Reject();
        contribution.RejectionReason.Should().BeNull();
    }

    [Fact]
    public void Reject_overlong_reason_throws()
    {
        var contribution = FundContribution.Report(FundId, MemberId, 100m);
        var act = () => contribution.Reject(new string('x', 1001));
        act.Should().Throw<InvalidContributionException>();
    }

    [Fact]
    public void Reject_already_confirmed_throws()
    {
        var contribution = FundContribution.Report(FundId, MemberId, 100m);
        contribution.Confirm();

        var act = () => contribution.Reject("Too late");
        act.Should().Throw<InvalidContributionException>();
    }
}
