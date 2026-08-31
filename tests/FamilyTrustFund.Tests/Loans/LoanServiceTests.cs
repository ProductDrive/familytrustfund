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

    private static (FakeFundRepository funds, FakeMembershipRepository members, FakeLoanRepository loans, LoanService service) Setup()
    {
        var funds = new FakeFundRepository();
        var members = new FakeMembershipRepository();
        var loans = new FakeLoanRepository();
        var service = new LoanService(loans, funds, members, new FakeContributionRepository(), new FakeAuditLog());
        return (funds, members, loans, service);
    }

    private static (FakeFundRepository funds, FakeMembershipRepository members, FakeLoanRepository loans, FakeContributionRepository contributions, LoanService service) SetupExposeContributions()
    {
        var funds = new FakeFundRepository();
        var members = new FakeMembershipRepository();
        var loans = new FakeLoanRepository();
        var contributions = new FakeContributionRepository();
        var service = new LoanService(loans, funds, members, contributions, new FakeAuditLog());
        return (funds, members, loans, contributions, service);
    }

    private static Fund CreateFamilyFund(
        Guid? guarantorId = null,
        decimal committedCapital = 1_000_000m)
    {
        return Fund.Create(
            guarantorId ?? GuarantorId,
            "Aunties Fund",
            FundType.Family,
            committedCapital,
            "ABCD1234");
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
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

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
        loans.ActiveDisbursedCounts[(MemberId, fund.Id)] = 1;

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
    public async Task RequestLoan_pending_request_exists_throws()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
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
    public async Task RequestLoan_exceeds_capacity_throws()
    {
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund(committedCapital: 100_000m);
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);
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
        var (funds, members, loans, service) = Setup();
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

        // Create a pending loan.
        var loan = Loan.Request(fund.Id, MemberId, 500_000m, RepaymentFrequency.Monthly, 0m, LoanFundingSource.GuarantorCapital);
        loans.Loans.Add(loan);

        var result = await service.ApproveLoanAsync(GuarantorId, new ApproveLoanRequest
        {
            LoanId = loan.Id,
            ApprovedAmount = 500_000m,
            ApprovedFrequency = RepaymentFrequency.Biweekly,
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
    public async Task RequestLoan_records_audit_event()
    {
        var (funds, members, loans, _) = Setup();
        var audit = new FakeAuditLog();
        var service = new LoanService(loans, funds, members, new FakeContributionRepository(), audit);
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

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
        var (funds, members, loans, _) = Setup();
        var audit = new FakeAuditLog();
        var service = new LoanService(loans, funds, members, new FakeContributionRepository(), audit);
        var fund = CreateFamilyFund();
        funds.Funds.Add(fund);
        AddActiveMembership(members, fund.Id, MemberId);

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
        var service = new LoanService(loans, funds, members, new FakeContributionRepository(), audit);
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
}
