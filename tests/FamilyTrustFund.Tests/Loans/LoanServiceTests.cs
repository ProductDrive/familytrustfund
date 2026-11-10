using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Application.Loans;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Membership;
using FamilyTrustFund.Tests.Support;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Loans;

public class LoanServiceTests
{
    private static readonly Guid GuarantorId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    private static LoanService CreateService(
        FakeLoanRepository loans,
        FakeFundRepository funds,
        FakeMembershipRepository members,
        FakeContributionRepository contributions,
        FakeAuditLog? audit = null)
    {
        var guarantors = new FakeFundGuarantorRepository(funds);
        return new LoanService(
            loans,
            funds,
            members,
            contributions,
            new FakeFundAccessService(funds, guarantors),
            audit ?? new FakeAuditLog(),
            new GuarantorNotificationService(new FakeEmailSender(), guarantors));
    }

    private static (FakeFundRepository funds, FakeMembershipRepository members, FakeLoanRepository loans, LoanService service) Setup()
    {
        var funds = new FakeFundRepository();
        var members = new FakeMembershipRepository();
        var loans = new FakeLoanRepository();
        var service = CreateService(loans, funds, members, new FakeContributionRepository());
        return (funds, members, loans, service);
    }

    private static (FakeFundRepository funds, FakeMembershipRepository members, FakeLoanRepository loans, FakeContributionRepository contributions, LoanService service) SetupExposeContributions()
    {
        var funds = new FakeFundRepository();
        var members = new FakeMembershipRepository();
        var loans = new FakeLoanRepository();
        var contributions = new FakeContributionRepository();
        var service = CreateService(loans, funds, members, contributions);
        return (funds, members, loans, contributions, service);
    }

    private static Fund CreateFamilyFund(
        Guid? guarantorId = null,
        decimal committedCapital = 1_000_000m,
        int? minLoanTermMonths = null,
        int? maxLoanTermMonths = null)
    {
        return Fund.Create(
            guarantorId ?? GuarantorId,
            "Aunties Fund",
            FundType.Family,
            committedCapital,
            "ABCD1234",
            minLoanTermMonths: minLoanTermMonths,
            maxLoanTermMonths: maxLoanTermMonths);
    }

    private static Fund CreateExternalFund(
        Guid? guarantorId = null,
        decimal committedCapital = 1_000_000m,
        decimal interestRate = 15m)
    {
        return Fund.Create(
            guarantorId ?? GuarantorId,
            "Biz Fund",
            FundType.External,
            committedCapital,
            "EFGH5678",
            interestRate: interestRate);
    }

    private static void AddActiveMembership(FakeMembershipRepository members, Guid fundId, Guid memberId)
    {
        var membership = FundMember.Join(fundId, memberId);
        members.Memberships.Add(membership);
    }

    [Fact]
    public async Task RequestLoan_valid_family_fund_creates_pending_loan()
    {
        var (funds, members, loans, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        var result = await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = fund.Id,
            Amount = 100_000m,
            Frequency = RepaymentFrequency.Monthly,
            Purpose = "School fees",
        });

        result.Status.Should().Be(LoanStatus.Pending);
        result.RequestedAmount.Should().Be(100_000m);
        result.InterestRate.Should().Be(0m);
        result.FundName.Should().Be("Aunties Fund");
        result.Purpose.Should().Be("School fees");
        loans.Loans.Should().ContainSingle(l => l.MemberId == MemberId);
    }

    [Fact]
    public async Task RequestLoan_external_fund_captures_interest_rate()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateExternalFund(interestRate: 15m);
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var result = await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = fund.Id,
            Amount = 50_000m,
            Frequency = RepaymentFrequency.Weekly,
        });

        result.InterestRate.Should().Be(15m);
    }

    [Fact]
    public async Task RequestLoan_fund_not_found_throws()
    {
        var (_, _, _, service) = Setup();

        var act = async () => await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = Guid.NewGuid(),
            Amount = 100_000m,
            Frequency = RepaymentFrequency.Monthly,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*Fund not found*");
    }

    [Fact]
    public async Task RequestLoan_inactive_fund_throws()
    {
        var (funds, members, _, service) = Setup();
        var fund = CreateFamilyFund();
        fund.SetStatus(FundStatus.Inactive);
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var act = async () => await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = fund.Id,
            Amount = 100_000m,
            Frequency = RepaymentFrequency.Monthly,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*not currently active*");
    }

    [Fact]
    public async Task RequestLoan_non_member_throws()
    {
        var (funds, _, _, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);

        var act = async () => await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = fund.Id,
            Amount = 100_000m,
            Frequency = RepaymentFrequency.Monthly,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*not an active member*");
    }

    [Fact]
    public async Task RequestLoan_active_disbursed_loan_throws()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var existing = Loan.Request(fund.Id, MemberId, 80_000m, RepaymentFrequency.Monthly, 0m, LoanFundingSource.GuarantorCapital);
        existing.Approve(GuarantorId, 80_000m, RepaymentFrequency.Monthly, 80_000m);
        existing.MarkDisbursementPending();
        existing.MarkDisbursed();
        loans.Loans.Add(existing);

        var act = async () => await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = fund.Id,
            Amount = 100_000m,
            Frequency = RepaymentFrequency.Monthly,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*already have an active loan*");
    }

    [Fact]
    public async Task RequestLoan_active_loan_in_other_fund_throws()
    {
        var (funds, members, loans, contributions, service) = SetupExposeContributions();
        var thisFund = CreateFamilyFund();
        var otherFund = CreateFamilyFund();
        funds.Funds.Add(thisFund);
        funds.Funds.Add(otherFund);
        AddActiveMembership(members, thisFund.Id, MemberId);
        AddActiveMembership(members, otherFund.Id, MemberId);
        // Active loan exists in a separate fund.
        var existing = Loan.Request(otherFund.Id, MemberId, 80_000m, RepaymentFrequency.Monthly, 0m, LoanFundingSource.GuarantorCapital);
        existing.Approve(GuarantorId, 80_000m, RepaymentFrequency.Monthly, 80_000m);
        existing.MarkDisbursementPending();
        existing.MarkDisbursed();
        loans.Loans.Add(existing);
        contributions.ConfirmedFundCredits[(MemberId, thisFund.Id)] = 200_000m;

        var act = async () => await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = thisFund.Id,
            Amount = 100_000m,
            Frequency = RepaymentFrequency.Monthly,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*already have an active loan*");
    }

    [Fact]
    public async Task RequestLoan_family_fund_without_contribution_throws()
    {
        var (funds, members, _, _, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        // No confirmed Fund Credit for the member in this fund.

        var act = async () => await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = fund.Id,
            Amount = 100_000m,
            Frequency = RepaymentFrequency.Monthly,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*contribute to this Family fund*");
    }

    [Fact]
    public async Task RequestLoan_family_fund_with_contribution_allowed()
    {
        var (funds, members, loans, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        var result = await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = fund.Id,
            Amount = 100_000m,
            Frequency = RepaymentFrequency.Monthly,
        });

        result.Status.Should().Be(LoanStatus.Pending);
        loans.Loans.Should().ContainSingle(l => l.MemberId == MemberId);
    }

    [Fact]
    public async Task RequestLoan_pending_request_exists_throws()
    {
        var (funds, members, loans, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;
        loans.PendingRequests.Add((MemberId, fund.Id));

        var act = async () => await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = fund.Id,
            Amount = 100_000m,
            Frequency = RepaymentFrequency.Monthly,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*pending loan request*");
    }

    [Fact]
    public async Task RequestLoan_pending_loan_in_other_fund_throws()
    {
        var (funds, members, loans, contributions, service) = SetupExposeContributions();
        var thisFund = CreateFamilyFund();
        var otherFund = CreateFamilyFund();
        funds.Funds.Add(thisFund);
        funds.Funds.Add(otherFund);
        AddActiveMembership(members, thisFund.Id, MemberId);
        AddActiveMembership(members, otherFund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, thisFund.Id)] = 200_000m;

        // A pending (not yet approved) request in another fund still blocks.
        var pending = Loan.Request(otherFund.Id, MemberId, 80_000m, RepaymentFrequency.Monthly, 0m, LoanFundingSource.GuarantorCapital);
        loans.Loans.Add(pending);

        var act = async () => await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = thisFund.Id,
            Amount = 100_000m,
            Frequency = RepaymentFrequency.Monthly,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*already have an active loan*");
    }

    [Fact]
    public async Task RequestLoan_approved_loan_in_other_fund_throws()
    {
        var (funds, members, loans, contributions, service) = SetupExposeContributions();
        var thisFund = CreateFamilyFund();
        var otherFund = CreateFamilyFund();
        funds.Funds.Add(thisFund);
        funds.Funds.Add(otherFund);
        AddActiveMembership(members, thisFund.Id, MemberId);
        AddActiveMembership(members, otherFund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, thisFund.Id)] = 200_000m;

        // An approved-but-not-disbursed loan anywhere blocks a new request.
        var approved = Loan.Request(otherFund.Id, MemberId, 80_000m, RepaymentFrequency.Monthly, 0m, LoanFundingSource.GuarantorCapital);
        approved.Approve(GuarantorId, 80_000m, RepaymentFrequency.Monthly, 80_000m);
        loans.Loans.Add(approved);

        var act = async () => await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = thisFund.Id,
            Amount = 100_000m,
            Frequency = RepaymentFrequency.Monthly,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*already have an active loan*");
    }

    [Fact]
    public async Task RequestLoan_cancelled_loan_does_not_block()
    {
        var (funds, members, loans, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        // A cancelled loan is terminal and must not block a fresh request.
        var cancelled = Loan.Request(fund.Id, MemberId, 80_000m, RepaymentFrequency.Monthly, 0m, LoanFundingSource.GuarantorCapital);
        cancelled.CancelByMember(MemberId);
        loans.Loans.Add(cancelled);

        var result = await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = fund.Id,
            Amount = 100_000m,
            Frequency = RepaymentFrequency.Monthly,
        });

        result.Status.Should().Be(LoanStatus.Pending);
    }

    [Fact]
    public async Task RequestLoan_exceeds_capacity_throws()
    {
        var (funds, members, loans, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund(committedCapital: 100_000m);
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;
        loans.DisbursedTotals[fund.Id] = 90_000m;

        var act = async () => await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = fund.Id,
            Amount = 20_000m,
            Frequency = RepaymentFrequency.Monthly,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*lending capacity*");
    }

    [Fact]
    public async Task ApproveLoan_sets_approved_terms()
    {
        var (funds, members, loans, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        // Create a pending loan.
        var loan = Loan.Request(fund.Id, MemberId, 500_000m, RepaymentFrequency.Monthly, 0m, LoanFundingSource.GuarantorCapital);
        loans.Loans.Add(loan);

        var result = await service.ApproveLoanAsync(GuarantorId, new ApproveLoanRequest
        {
            LoanId = loan.Id,
            ApprovedAmount = 500_000m,
            ApprovedFrequency = RepaymentFrequency.Biweekly,
            ConfirmedWithMember = true,
        });

        result.Status.Should().Be(LoanStatus.Approved);
        result.ApprovedAmount.Should().Be(500_000m);
        result.ApprovedFrequency.Should().Be(RepaymentFrequency.Biweekly);
        result.TotalRepayable.Should().Be(500_000m); // Family: no interest
    }

    [Fact]
    public async Task ApproveLoan_non_owner_throws()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var loan = Loan.Request(fund.Id, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loans.Loans.Add(loan);

        var otherGuarantor = Guid.NewGuid();
        var act = async () => await service.ApproveLoanAsync(otherGuarantor, new ApproveLoanRequest
        {
            LoanId = loan.Id,
            ApprovedAmount = 100m,
            ApprovedFrequency = RepaymentFrequency.Weekly,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*permission*");
    }

    [Fact]
    public async Task ApproveLoan_non_pending_throws()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var loan = Loan.Request(fund.Id, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loan.Approve(GuarantorId, 100m, RepaymentFrequency.Weekly, 100m);
        loans.Loans.Add(loan);

        var act = async () => await service.ApproveLoanAsync(GuarantorId, new ApproveLoanRequest
        {
            LoanId = loan.Id,
            ApprovedAmount = 100m,
            ApprovedFrequency = RepaymentFrequency.Weekly,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*pending*");
    }

    [Fact]
    public async Task ApproveLoan_exceeds_capacity_throws()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund(committedCapital: 100_000m);
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        loans.DisbursedTotals[fund.Id] = 90_000m;

        var loan = Loan.Request(fund.Id, MemberId, 20_000m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loans.Loans.Add(loan);

        var act = async () => await service.ApproveLoanAsync(GuarantorId, new ApproveLoanRequest
        {
            LoanId = loan.Id,
            ApprovedAmount = 20_000m,
            ApprovedFrequency = RepaymentFrequency.Weekly,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*lending capacity*");
    }

    [Fact]
    public async Task ApproveLoan_calculates_total_repayable_for_external_fund()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateExternalFund(interestRate: 10m);
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var loan = Loan.Request(fund.Id, MemberId, 200_000m, RepaymentFrequency.Monthly, 10m, LoanFundingSource.GuarantorCapital);
        loans.Loans.Add(loan);

        var result = await service.ApproveLoanAsync(GuarantorId, new ApproveLoanRequest
        {
            LoanId = loan.Id,
            ApprovedAmount = 200_000m,
            ApprovedFrequency = RepaymentFrequency.Monthly,
        });

        // 200,000 + (200,000 × 10 / 100) = 220,000
        result.TotalRepayable.Should().Be(220_000m);
        result.InterestRate.Should().Be(10m);
    }

    [Fact]
    public async Task RejectLoan_sets_rejection_reason()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var loan = Loan.Request(fund.Id, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loans.Loans.Add(loan);

        var result = await service.RejectLoanAsync(GuarantorId, new RejectLoanRequest
        {
            LoanId = loan.Id,
            Reason = "Insufficient documentation",
        });

        result.Status.Should().Be(LoanStatus.Rejected);
        result.RejectionReason.Should().Be("Insufficient documentation");
    }

    [Fact]
    public async Task RejectLoan_non_owner_throws()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var loan = Loan.Request(fund.Id, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loans.Loans.Add(loan);

        var otherGuarantor = Guid.NewGuid();
        var act = async () => await service.RejectLoanAsync(otherGuarantor, new RejectLoanRequest
        {
            LoanId = loan.Id,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*permission*");
    }

    [Fact]
    public async Task CancelLoanByMember_cancels_pending_request()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var loan = Loan.Request(fund.Id, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loans.Loans.Add(loan);

        var result = await service.CancelLoanByMemberAsync(MemberId, loan.Id);

        result.Status.Should().Be(LoanStatus.Cancelled);
        result.CancelledAtUtc.Should().NotBeNull();
        loan.Status.Should().Be(LoanStatus.Cancelled);
    }

    [Fact]
    public async Task CancelLoanByMember_non_owner_throws()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var loan = Loan.Request(fund.Id, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loans.Loans.Add(loan);

        var act = async () => await service.CancelLoanByMemberAsync(Guid.NewGuid(), loan.Id);

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*own loan request*");
    }

    [Fact]
    public async Task CancelLoanByMember_approved_loan_throws()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var loan = Loan.Request(fund.Id, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loan.Approve(GuarantorId, 100m, RepaymentFrequency.Weekly, 100m);
        loans.Loans.Add(loan);

        var act = async () => await service.CancelLoanByMemberAsync(MemberId, loan.Id);

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*pending loan request*");
    }

    [Fact]
    public async Task CancelLoanByGuarantor_cancels_pending_and_approved()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var pending = Loan.Request(fund.Id, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loans.Loans.Add(pending);

        var approved = Loan.Request(fund.Id, MemberId, 200m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        approved.Approve(GuarantorId, 200m, RepaymentFrequency.Weekly, 200m);
        loans.Loans.Add(approved);

        var pendingResult = await service.CancelLoanByGuarantorAsync(GuarantorId, pending.Id);
        var approvedResult = await service.CancelLoanByGuarantorAsync(GuarantorId, approved.Id);

        pendingResult.Status.Should().Be(LoanStatus.Cancelled);
        approvedResult.Status.Should().Be(LoanStatus.Cancelled);
    }

    [Fact]
    public async Task CancelLoanByGuarantor_non_owner_throws()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var loan = Loan.Request(fund.Id, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loans.Loans.Add(loan);

        var act = async () => await service.CancelLoanByGuarantorAsync(Guid.NewGuid(), loan.Id);

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*permission to cancel*");
    }

    [Fact]
    public async Task CancelLoanByGuarantor_disbursed_loan_throws()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var loan = Loan.Request(fund.Id, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loan.Approve(GuarantorId, 100m, RepaymentFrequency.Weekly, 100m);
        loan.MarkDisbursementPending();
        loan.MarkDisbursed();
        loans.Loans.Add(loan);

        var act = async () => await service.CancelLoanByGuarantorAsync(GuarantorId, loan.Id);

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*not yet disbursed*");
    }

    [Fact]
    public async Task CancelLoan_records_audit_event()
    {
        var (funds, members, loans, _) = Setup();
        var audit = new FakeAuditLog();
        var service = CreateService(loans, funds, members, new FakeContributionRepository(), audit);
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var loan = Loan.Request(fund.Id, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loans.Loans.Add(loan);

        await service.CancelLoanByMemberAsync(MemberId, loan.Id);

        audit.Events.Should().Contain(e =>
            e.Action == "Loan.Cancelled"
            && e.ActorId == MemberId
            && e.ResourceType == "Loan");
    }

    [Fact]
    public async Task RequestLoan_records_audit_event()
    {
        var (funds, members, loans, _) = Setup();
        var contributions = new FakeContributionRepository();
        var audit = new FakeAuditLog();
        var service = CreateService(loans, funds, members, contributions, audit);
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = fund.Id,
            Amount = 100_000m,
            Frequency = RepaymentFrequency.Monthly,
        });

        audit.Events.Should().Contain(e =>
            e.Action == "Loan.Requested"
            && e.ActorId == MemberId
            && e.ResourceType == "Loan");
    }

    [Fact]
    public async Task ApproveLoan_records_audit_event()
    {
        var (funds, members, loans, contributions, _) = SetupExposeContributions();
        var audit = new FakeAuditLog();
        var service = CreateService(loans, funds, members, contributions, audit);
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        var loan = Loan.Request(fund.Id, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loans.Loans.Add(loan);

        await service.ApproveLoanAsync(GuarantorId, new ApproveLoanRequest
        {
            LoanId = loan.Id,
            ApprovedAmount = 100m,
            ApprovedFrequency = RepaymentFrequency.Weekly,
        });

        audit.Events.Should().Contain(e =>
            e.Action == "Loan.Approved"
            && e.ActorId == GuarantorId
            && e.ResourceType == "Loan");
    }

    [Fact]
    public async Task RejectLoan_records_audit_event()
    {
        var (funds, members, loans, _) = Setup();
        var audit = new FakeAuditLog();
        var service = CreateService(loans, funds, members, new FakeContributionRepository(), audit);
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var loan = Loan.Request(fund.Id, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loans.Loans.Add(loan);

        await service.RejectLoanAsync(GuarantorId, new RejectLoanRequest
        {
            LoanId = loan.Id,
            Reason = "Not eligible",
        });

        audit.Events.Should().Contain(e =>
            e.Action == "Loan.Rejected"
            && e.ActorId == GuarantorId
            && e.ResourceType == "Loan");
    }

    [Fact]
    public async Task GetPendingRequestsForGuarantor_returns_pending_loans()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);

        var loan = Loan.Request(fund.Id, MemberId, 100m, RepaymentFrequency.Weekly, 0m, LoanFundingSource.GuarantorCapital);
        loans.Loans.Add(loan);

        var result = await service.GetPendingRequestsForGuarantorAsync(GuarantorId);

        result.Should().ContainSingle(l => l.Id == loan.Id && l.Status == LoanStatus.Pending);
    }

    [Fact]
    public async Task GetLendingCapacity_returns_correct_values()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund(committedCapital: 500_000m);
        funds.Funds.Add(fund);
        loans.DisbursedTotals[fund.Id] = 200_000m;

        var capacity = await service.GetLendingCapacityAsync(fund.Id, MemberId);

        capacity.CommittedCapital.Should().Be(500_000m);
        capacity.ActiveDisbursedLoans.Should().Be(200_000m);
        capacity.AvailableLendingCapacity.Should().Be(300_000m);
    }

    [Fact]
    public async Task GetLendingCapacity_family_fund_shows_borrowing_entitlement()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        var capacity = await service.GetLendingCapacityAsync(fund.Id, MemberId);

        capacity.FamilyBorrowingEntitlement.Should().NotBeNull();
        capacity.FundCredit.Should().NotBeNull();
    }

    [Fact]
    public async Task GetLendingCapacity_borrowing_entitlement_derived_from_fund_credit()
    {
        var (funds, members, loans, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 250_000m;

        var capacity = await service.GetLendingCapacityAsync(fund.Id, MemberId);

        capacity.FundCredit.Should().Be(250_000m);
        // Default multiplier is 10x.
        capacity.FamilyBorrowingEntitlement.Should().Be(2_500_000m);
    }

    // ── CalculateTotalRepayable unit tests ──────────────────────────────

    [Fact]
    public void CalculateTotalRepayable_no_interest_returns_principal()
    {
        LoanService.CalculateTotalRepayable(100_000m, 0m).Should().Be(100_000m);
    }

    [Fact]
    public void CalculateTotalRepayable_with_interest_adds_correctly()
    {
        // 200,000 + (200,000 × 15 / 100) = 230,000
        LoanService.CalculateTotalRepayable(200_000m, 15m).Should().Be(230_000m);
    }

    [Fact]
    public void CalculateTotalRepayable_fractional_interest_rounds()
    {
        // 100,000 × 7.5 / 100 = 7,500.00
        LoanService.CalculateTotalRepayable(100_000m, 7.5m).Should().Be(107_500m);
    }

    [Fact]
    public void CalculateTotalRepayable_zero_principal_throws()
    {
        var act = () => LoanService.CalculateTotalRepayable(0m, 10m);
        act.Should().Throw<InvalidLoanException>();
    }

    [Fact]
    public void CalculateTotalRepayable_negative_interest_throws()
    {
        var act = () => LoanService.CalculateTotalRepayable(100m, -5m);
        act.Should().Throw<InvalidLoanException>();
    }

    [Fact]
    public async Task RequestLoan_captures_requested_term_and_instalments()
    {
        var (funds, members, loans, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        var result = await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = fund.Id,
            Amount = 24_000m,
            Frequency = RepaymentFrequency.Weekly,
            TermMonths = 6,
            Instalments = 26,
        });

        result.RequestedTermMonths.Should().Be(6);
        result.RequestedInstalments.Should().Be(26);
        result.RequestedFrequency.Should().Be(RepaymentFrequency.Weekly);
        loans.Loans.Should().ContainSingle(l => l.RequestedTermMonths == 6 && l.RequestedInstalments == 26);
    }

    [Fact]
    public async Task RequestLoan_rejects_term_outside_fund_range()
    {
        var (funds, members, _, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund(minLoanTermMonths: 3, maxLoanTermMonths: 12);
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        var act = async () => await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = fund.Id,
            Amount = 10_000m,
            Frequency = RepaymentFrequency.Monthly,
            TermMonths = 2,
            Instalments = 2,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*between 3 and 12 months*");
    }

    [Fact]
    public async Task RequestLoan_rejects_instalment_count_mismatch()
    {
        var (funds, members, _, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        var act = async () => await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = fund.Id,
            Amount = 10_000m,
            Frequency = RepaymentFrequency.Monthly,
            TermMonths = 6,
            Instalments = 5,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*exactly 6*");
    }

    [Fact]
    public async Task RequestLoan_rejects_amount_above_family_entitlement()
    {
        var (funds, members, _, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 1_000m;

        var act = async () => await service.RequestLoanAsync(MemberId, new RequestLoanRequest
        {
            FundId = fund.Id,
            Amount = 20_000m,
            Frequency = RepaymentFrequency.Monthly,
            TermMonths = 6,
            Instalments = 6,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*borrowing entitlement*");
    }

    [Fact]
    public async Task PreviewLoan_returns_server_calculated_breakdown()
    {
        var (funds, members, _, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        var preview = await service.PreviewLoanAsync(MemberId, new LoanPreviewRequest
        {
            FundId = fund.Id,
            Amount = 24_000m,
            Frequency = RepaymentFrequency.Weekly,
            TermMonths = 6,
            Instalments = 26,
        });

        preview.IsValid.Should().BeTrue();
        preview.Errors.Should().BeEmpty();
        preview.TotalRepayable.Should().Be(24_000m);
        preview.InterestAmount.Should().Be(0m);
        preview.Instalments.Should().Be(26);
        preview.InstalmentAmount.Should().Be(923.08m);
        preview.FinalInstalmentAmount.Should().Be(923.00m);
        preview.FirstDueDateUtc.Should().NotBeNull();
        preview.FinalDueDateUtc.Should().NotBeNull();
        preview.AvailableLendingCapacity.Should().Be(1_000_000m);
        preview.FamilyBorrowingEntitlement.Should().Be(2_000_000m);
        preview.MinTermMonths.Should().Be(1);
        preview.MaxTermMonths.Should().Be(60);
    }

    [Fact]
    public async Task PreviewLoan_reports_invalid_instalment_count()
    {
        var (funds, members, _, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        var preview = await service.PreviewLoanAsync(MemberId, new LoanPreviewRequest
        {
            FundId = fund.Id,
            Amount = 24_000m,
            Frequency = RepaymentFrequency.Monthly,
            TermMonths = 6,
            Instalments = 5,
        });

        preview.IsValid.Should().BeFalse();
        preview.Errors.Should().ContainSingle(e => e.Contains("exactly 6"));
    }

    [Fact]
    public async Task PreviewLoan_reports_absurd_instalment_count_without_building_a_schedule()
    {
        var (funds, members, _, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        var preview = await service.PreviewLoanAsync(MemberId, new LoanPreviewRequest
        {
            FundId = fund.Id,
            Amount = 24_000m,
            Frequency = RepaymentFrequency.Monthly,
            TermMonths = 6,
            Instalments = 1_000_000,
        });

        preview.IsValid.Should().BeFalse();
        preview.Errors.Should().Contain(e => e.Contains("exactly 6"));
        preview.InstalmentAmount.Should().Be(0m);
        preview.FirstDueDateUtc.Should().BeNull();
    }

    [Fact]
    public async Task PreviewLoan_reports_entitlement_error()
    {
        var (funds, members, _, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 1_000m;

        var preview = await service.PreviewLoanAsync(MemberId, new LoanPreviewRequest
        {
            FundId = fund.Id,
            Amount = 20_000m,
            Frequency = RepaymentFrequency.Monthly,
            TermMonths = 6,
            Instalments = 6,
        });

        preview.IsValid.Should().BeFalse();
        preview.Errors.Should().Contain(e => e.Contains("borrowing entitlement"));
    }

    [Fact]
    public async Task ApproveLoan_rejects_changed_terms_without_acknowledgement()
    {
        var (funds, members, loans, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        var loan = Loan.Request(
            fund.Id, MemberId, 120_000m, RepaymentFrequency.Monthly, 0m,
            LoanFundingSource.GuarantorCapital, termMonths: 6, instalments: 6);
        loans.Loans.Add(loan);

        var act = async () => await service.ApproveLoanAsync(GuarantorId, new ApproveLoanRequest
        {
            LoanId = loan.Id,
            ApprovedAmount = 120_000m,
            ApprovedFrequency = RepaymentFrequency.Monthly,
            ApprovedTermMonths = 12,
            ApprovedInstalments = 12,
            ConfirmedWithMember = false,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*Confirm the new arrangement*");
    }

    [Fact]
    public async Task ApproveLoan_with_acknowledgement_freezes_approved_terms()
    {
        var (funds, members, loans, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        var loan = Loan.Request(
            fund.Id, MemberId, 120_000m, RepaymentFrequency.Monthly, 0m,
            LoanFundingSource.GuarantorCapital, termMonths: 6, instalments: 6);
        loans.Loans.Add(loan);

        var result = await service.ApproveLoanAsync(GuarantorId, new ApproveLoanRequest
        {
            LoanId = loan.Id,
            ApprovedAmount = 120_000m,
            ApprovedFrequency = RepaymentFrequency.Monthly,
            ApprovedTermMonths = 12,
            ApprovedInstalments = 12,
            ConfirmedWithMember = true,
        });

        result.Status.Should().Be(LoanStatus.Approved);
        result.ApprovedTermMonths.Should().Be(12);
        result.RepaymentTerm.Should().Be(12);
        result.RequestedTermMonths.Should().Be(6);
    }

    [Fact]
    public async Task ApproveLoan_rejects_term_outside_fund_range()
    {
        var (funds, members, loans, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund(minLoanTermMonths: 3, maxLoanTermMonths: 12);
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        var loan = Loan.Request(
            fund.Id, MemberId, 24_000m, RepaymentFrequency.Monthly, 0m,
            LoanFundingSource.GuarantorCapital, termMonths: 6, instalments: 6);
        loans.Loans.Add(loan);

        var act = async () => await service.ApproveLoanAsync(GuarantorId, new ApproveLoanRequest
        {
            LoanId = loan.Id,
            ApprovedAmount = 24_000m,
            ApprovedFrequency = RepaymentFrequency.Monthly,
            ApprovedTermMonths = 24,
            ApprovedInstalments = 24,
            ConfirmedWithMember = true,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*between 3 and 12 months*");
    }

    [Fact]
    public async Task ApproveLoan_rejects_amount_above_family_entitlement()
    {
        var (funds, members, loans, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 1_000m;

        var loan = Loan.Request(
            fund.Id, MemberId, 24_000m, RepaymentFrequency.Monthly, 0m,
            LoanFundingSource.GuarantorCapital, termMonths: 6, instalments: 6);
        loans.Loans.Add(loan);

        var act = async () => await service.ApproveLoanAsync(GuarantorId, new ApproveLoanRequest
        {
            LoanId = loan.Id,
            ApprovedAmount = 24_000m,
            ApprovedFrequency = RepaymentFrequency.Monthly,
            ApprovedTermMonths = 6,
            ApprovedInstalments = 6,
        });

        await act.Should().ThrowAsync<InvalidLoanException>()
            .WithMessage("*borrowing entitlement*");
    }

    [Fact]
    public async Task PreviewApproval_reports_changed_terms()
    {
        var (funds, members, loans, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        var loan = Loan.Request(
            fund.Id, MemberId, 24_000m, RepaymentFrequency.Weekly, 0m,
            LoanFundingSource.GuarantorCapital, termMonths: 6, instalments: 26);
        loans.Loans.Add(loan);

        var preview = await service.PreviewApprovalAsync(GuarantorId, new GuarantorLoanPreviewRequest
        {
            LoanId = loan.Id,
            ApprovedAmount = 30_000m,
            ApprovedFrequency = RepaymentFrequency.Weekly,
            ApprovedTermMonths = 6,
            ApprovedInstalments = 26,
        });

        preview.TermsChanged.Should().BeTrue();
        preview.Changes.Should().ContainSingle(c => c.Field == "Amount");
        preview.Preview.IsValid.Should().BeTrue();
        preview.Preview.Amount.Should().Be(30_000m);
    }

    [Fact]
    public async Task PreviewApproval_reports_no_change_when_terms_match_request()
    {
        var (funds, members, loans, contributions, service) = SetupExposeContributions();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
        contributions.ConfirmedFundCredits[(MemberId, fund.Id)] = 200_000m;

        var loan = Loan.Request(
            fund.Id, MemberId, 24_000m, RepaymentFrequency.Weekly, 0m,
            LoanFundingSource.GuarantorCapital, termMonths: 6, instalments: 26);
        loans.Loans.Add(loan);

        var preview = await service.PreviewApprovalAsync(GuarantorId, new GuarantorLoanPreviewRequest
        {
            LoanId = loan.Id,
            ApprovedAmount = 24_000m,
            ApprovedFrequency = RepaymentFrequency.Weekly,
            ApprovedTermMonths = 6,
            ApprovedInstalments = 26,
        });

        preview.TermsChanged.Should().BeFalse();
        preview.Changes.Should().BeEmpty();
    }
}
