using FamilyTrustFund.Application.Contributions;
using FamilyTrustFund.Application.Evidence;
using FamilyTrustFund.Domain.Contributions;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Membership;
using FamilyTrustFund.Tests.Support;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Contributions;

public class ContributionServiceTests
{
    private static readonly Guid GuarantorId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    private static (
        FakeFundRepository funds,
        FakeMembershipRepository members,
        FakeContributionRepository contributions,
        ContributionService service) Setup()
    {
        var funds = new FakeFundRepository();
        var members = new FakeMembershipRepository();
        var contributions = new FakeContributionRepository();
        var service = new ContributionService(contributions, funds, members, new FakeEvidenceRepository(), new FakeAuditLog());
        return (funds, members, contributions, service);
    }

    private static Fund CreateFamilyFund(
        Guid? guarantorId = null,
        decimal committedCapital = 1_000_000m) =>
        Fund.Create(guarantorId ?? GuarantorId, "Aunties Fund", FundType.Family, committedCapital, "ABCD1234");

    private static Fund CreateExternalFund(
        Guid? guarantorId = null,
        decimal committedCapital = 1_000_000m,
        decimal interestRate = 15m) =>
        Fund.Create(guarantorId ?? GuarantorId, "Biz Fund", FundType.External, committedCapital, "EFGH5678", interestRate: interestRate);

    private static void AddActiveMembership(FakeMembershipRepository members, Guid fundId, Guid memberId)
    {
        members.Memberships.Add(FundMember.Join(fundId, memberId));
    }

    [Fact]
    public async Task Report_valid_family_fund_creates_pending_contribution()
    {
        var (funds, members, contributions, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var result = await service.ReportContributionAsync(MemberId, new ReportContributionRequest
        {
            FundId = fund.Id,
            Amount = 100_000m,
            Reference = "FT123",
            Note = "Monthly savings",
        });

        result.Status.Should().Be(ContributionStatus.PendingConfirmation);
        result.Amount.Should().Be(100_000m);
        result.FundName.Should().Be("Aunties Fund");
        contributions.Contributions.Should().ContainSingle(c => c.MemberId == MemberId);
    }

    [Fact]
    public async Task Report_fund_not_found_throws()
    {
        var (_, _, _, service) = Setup();

        var act = async () => await service.ReportContributionAsync(MemberId, new ReportContributionRequest
        {
            FundId = Guid.NewGuid(),
            Amount = 100m,
        });

        await act.Should().ThrowAsync<InvalidContributionException>()
            .WithMessage("*Fund not found*");
    }

    [Fact]
    public async Task Report_external_fund_throws()
    {
        var (funds, members, _, service) = Setup();
        var fund = CreateExternalFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var act = async () => await service.ReportContributionAsync(MemberId, new ReportContributionRequest
        {
            FundId = fund.Id,
            Amount = 100m,
        });

        await act.Should().ThrowAsync<InvalidContributionException>()
            .WithMessage("*Only Family funds accept contributions*");
    }

    [Fact]
    public async Task Report_not_an_active_member_throws()
    {
        var (funds, _, _, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);

        var act = async () => await service.ReportContributionAsync(MemberId, new ReportContributionRequest
        {
            FundId = fund.Id,
            Amount = 100m,
        });

        await act.Should().ThrowAsync<InvalidContributionException>()
            .WithMessage("*not an active member*");
    }

    [Fact]
    public async Task Report_with_active_loan_throws()
    {
        var (funds, members, contributions, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ActiveLoans.Add((MemberId, fund.Id));

        var act = async () => await service.ReportContributionAsync(MemberId, new ReportContributionRequest
        {
            FundId = fund.Id,
            Amount = 100m,
        });

        await act.Should().ThrowAsync<InvalidContributionException>()
            .WithMessage("*active loan*");
    }

    [Fact]
    public async Task Report_with_active_loan_in_another_fund_throws()
    {
        // ADR-006 scope: the restriction is member-wide, not fund-scoped. A
        // member with an active loan in ANY fund cannot contribute to any fund.
        var (funds, members, contributions, service) = Setup();
        var otherFund = CreateFamilyFund();
        funds.Funds.Add(otherFund);
        var otherMembership = FundMember.Join(otherFund.Id, MemberId);
        members.Memberships.Add(otherMembership);
        contributions.ActiveLoans.Add((MemberId, otherFund.Id));

        var targetFund = CreateFamilyFund();
        funds.Funds.Add(targetFund);
        AddActiveMembership(members, targetFund.Id, MemberId);

        var act = async () => await service.ReportContributionAsync(MemberId, new ReportContributionRequest
        {
            FundId = targetFund.Id,
            Amount = 100m,
        });

        await act.Should().ThrowAsync<InvalidContributionException>()
            .WithMessage("*active loan*");
    }

    [Fact]
    public async Task Confirm_posts_contribution_when_guarantor_owns_fund()
    {
        var (funds, _, contributions, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        var contribution = FundContribution.Report(fund.Id, MemberId, 100_000m);
        contributions.Contributions.Add(contribution);

        var result = await service.ConfirmContributionAsync(GuarantorId, new ConfirmContributionRequest
        {
            ContributionId = contribution.Id,
        });

        result.Status.Should().Be(ContributionStatus.Confirmed);
        contribution.IsConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task Confirm_non_owner_guarantor_throws()
    {
        var (funds, _, contributions, service) = Setup();
        var fund = CreateFamilyFund(guarantorId: Guid.NewGuid());
        funds.Funds.Add(fund);
        var contribution = FundContribution.Report(fund.Id, MemberId, 100m);
        contributions.Contributions.Add(contribution);

        var act = async () => await service.ConfirmContributionAsync(GuarantorId, new ConfirmContributionRequest
        {
            ContributionId = contribution.Id,
        });

        await act.Should().ThrowAsync<InvalidContributionException>()
            .WithMessage("*permission to confirm*");
    }

    [Fact]
    public async Task Confirm_not_found_throws()
    {
        var (_, _, _, service) = Setup();

        var act = async () => await service.ConfirmContributionAsync(GuarantorId, new ConfirmContributionRequest
        {
            ContributionId = Guid.NewGuid(),
        });

        await act.Should().ThrowAsync<InvalidContributionException>()
            .WithMessage("*Contribution not found*");
    }

    [Fact]
    public async Task Confirm_already_confirmed_throws()
    {
        var (funds, _, contributions, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        var contribution = FundContribution.Report(fund.Id, MemberId, 100m);
        contribution.Confirm();
        contributions.Contributions.Add(contribution);

        var act = async () => await service.ConfirmContributionAsync(GuarantorId, new ConfirmContributionRequest
        {
            ContributionId = contribution.Id,
        });

        await act.Should().ThrowAsync<InvalidContributionException>();
    }

    [Fact]
    public async Task Reject_marks_contribution_rejected()
    {
        var (funds, _, contributions, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        var contribution = FundContribution.Report(fund.Id, MemberId, 100m);
        contributions.Contributions.Add(contribution);

        var result = await service.RejectContributionAsync(GuarantorId, new RejectContributionRequest
        {
            ContributionId = contribution.Id,
            Reason = "Duplicate",
        });

        result.Status.Should().Be(ContributionStatus.Rejected);
        contribution.RejectionReason.Should().Be("Duplicate");
    }

    [Fact]
    public async Task Reject_non_owner_guarantor_throws()
    {
        var (funds, _, contributions, service) = Setup();
        var fund = CreateFamilyFund(guarantorId: Guid.NewGuid());
        funds.Funds.Add(fund);
        var contribution = FundContribution.Report(fund.Id, MemberId, 100m);
        contributions.Contributions.Add(contribution);

        var act = async () => await service.RejectContributionAsync(GuarantorId, new RejectContributionRequest
        {
            ContributionId = contribution.Id,
        });

        await act.Should().ThrowAsync<InvalidContributionException>()
            .WithMessage("*permission to reject*");
    }

    [Fact]
    public async Task Confirm_and_Reject_mark_evidence_as_reviewed()
    {
        var funds = new FakeFundRepository();
        var members = new FakeMembershipRepository();
        var contributions = new FakeContributionRepository();
        var evidence = new FakeEvidenceRepository();
        var service = new ContributionService(contributions, funds, members, evidence, new FakeAuditLog());

        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);

        // Contribution with attached evidence.
        var contribution = FundContribution.Report(fund.Id, MemberId, 100m);
        contributions.Contributions.Add(contribution);
        var uploaded = await new EvidenceService(evidence, new FakeFileStorage(), new FakeAuditLog())
            .UploadAsync(MemberId, "Contribution", contribution.Id, "receipt.png", "image/png", new byte[] { 1, 2, 3 });

        var confirmed = await service.ConfirmContributionAsync(GuarantorId, new ConfirmContributionRequest
        {
            ContributionId = contribution.Id,
        });

        confirmed.Status.Should().Be(ContributionStatus.Confirmed);

        var storedAfterConfirm = evidence.Items.Single(i => i.Id == uploaded.Id);
        storedAfterConfirm.ReviewedByUserId.Should().Be(GuarantorId);
        storedAfterConfirm.ReviewedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMyFundCredit_returns_summary_with_entitlement()
    {
        var (funds, members, contributions, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        members.Memberships.Add(FundMember.Join(fund.Id, MemberId));
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        var summaries = await service.GetMyFundCreditAsync(MemberId);

        var summary = summaries.Single();
        summary.FundId.Should().Be(fund.Id);
        summary.FundCredit.Should().Be(200_000m);
        summary.BorrowingEntitlement.Should().Be(2_000_000m);
        summary.HasActiveDisbursedLoan.Should().BeFalse();
    }

    [Fact]
    public async Task GetMyFundCredit_external_fund_has_no_entitlement()
    {
        var (funds, members, contributions, service) = Setup();
        var fund = CreateExternalFund();
        funds.Funds.Add(fund);
        members.Memberships.Add(FundMember.Join(fund.Id, MemberId));
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 0m;

        var summaries = await service.GetMyFundCreditAsync(MemberId);

        summaries.Single().BorrowingEntitlement.Should().BeNull();
    }
}
