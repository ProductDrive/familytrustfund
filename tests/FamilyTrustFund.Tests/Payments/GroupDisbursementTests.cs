using FamilyTrustFund.Application.Payments;
using FamilyTrustFund.Application.Repayments;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Payments;
using FamilyTrustFund.Tests.Support;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Payments;

/// <summary>
/// Disbursement in a Group Guarantor fund: only the Fund Originator may initiate
/// or cancel, the fund needs at least two active Guarantors, and every required
/// Guarantor must have approved (ADR-052, ADR-053).
/// </summary>
public class GroupDisbursementTests
{
    private static readonly Guid OriginatorId = Guid.NewGuid();
    private static readonly Guid CoGuarantorId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    private sealed class Harness
    {
        public FakeFundRepository Funds { get; } = new();
        public FakeLoanRepository Loans { get; } = new();
        public FakePaymentRepository Payments { get; } = new();
        public FakeCapitalFundingRepository CapitalFunding { get; } = new();
        public FakePaymentProvider Provider { get; } = new();
        public FakeAuditLog Audit { get; } = new();
        public FakeRepaymentRepository Repayments { get; } = new();
        public FakeFundGuarantorRepository Guarantors { get; }
        public DisbursementService Service { get; }
        public Fund Fund { get; }

        public Harness()
        {
            Guarantors = new FakeFundGuarantorRepository(Funds);
            var registry = new FakePaymentProviderRegistry();
            registry.ByName[Provider.Name] = Provider;

            Service = new DisbursementService(
                Payments,
                registry,
                CapitalFunding,
                Loans,
                Funds,
                Guarantors,
                new RepaymentService(Repayments, Audit),
                Audit);

            Fund = Fund.Create(
                OriginatorId,
                "Family Group",
                FundType.Family,
                1_000_000m,
                "GROUP123",
                guarantorMode: FundGuarantorMode.Group);
            Funds.Add(Fund);

            Guarantors.Add(FundGuarantor.CreateOriginator(Fund.Id, OriginatorId, 1_000_000m));
            var invite = FundGuarantor.Invite(Fund.Id, CoGuarantorId, "co@example.com", 0m, OriginatorId);
            Guarantors.Add(invite);
            invite.Accept();
        }

        public Loan AddApprovedLoan(bool bothApproved = true, bool includeCoGuarantor = true)
        {
            var loan = Loan.Request(
                Fund.Id,
                MemberId,
                100_000m,
                RepaymentFrequency.Monthly,
                0m,
                LoanFundingSource.GuarantorCapital,
                termMonths: 12,
                instalments: 12);
            loan.FreezeApprovedTerms(OriginatorId, 100_000m, RepaymentFrequency.Monthly, 100_000m, 12, 12);
            loan.CompleteUnanimousApproval();
            Loans.Loans.Add(loan);
            Repayments.Loans.Add(loan);

            var originatorApproval = LoanApproval.Require(loan.Id, OriginatorId);
            originatorApproval.Approve();
            Loans.AddApproval(originatorApproval);

            if (includeCoGuarantor)
            {
                var coApproval = LoanApproval.Require(loan.Id, CoGuarantorId);
                if (bothApproved)
                {
                    coApproval.Approve();
                }

                Loans.AddApproval(coApproval);
            }

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

        public Task<DisbursementDto> InitiateAsync(Guid guarantorId, Guid loanId) =>
            Service.InitiateDisbursementAsync(
                guarantorId,
                new InitiateDisbursementRequest { LoanId = loanId },
                "guarantor@example.com",
                null,
                CancellationToken.None);
    }

    [Fact]
    public async Task Only_the_fund_originator_can_initiate_disbursement()
    {
        var h = new Harness();
        var loan = h.AddApprovedLoan();
        await h.AddVerifiedRecipientAsync();

        var act = () => h.InitiateAsync(CoGuarantorId, loan.Id);

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*Only the Fund Originator*");
    }

    [Fact]
    public async Task A_group_fund_needs_at_least_two_active_guarantors_to_disburse()
    {
        var h = new Harness();
        var loan = h.AddApprovedLoan();
        await h.AddVerifiedRecipientAsync();

        // The co-guarantor's membership is no longer active.
        h.Guarantors.Memberships.Single(m => m.GuarantorId == CoGuarantorId).Leave();

        var act = () => h.InitiateAsync(OriginatorId, loan.Id);

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*at least two active Guarantors*");
    }

    [Fact]
    public async Task Every_required_guarantor_must_approve_before_disbursement()
    {
        var h = new Harness();
        var loan = h.AddApprovedLoan(bothApproved: false);
        await h.AddVerifiedRecipientAsync();

        var act = () => h.InitiateAsync(OriginatorId, loan.Id);

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*Every active Guarantor must approve*");
    }

    [Fact]
    public async Task Unanimous_approval_lets_the_originator_disburse()
    {
        var h = new Harness();
        var loan = h.AddApprovedLoan();
        await h.AddVerifiedRecipientAsync();

        var dto = await h.InitiateAsync(OriginatorId, loan.Id);

        dto.Status.Should().Be(DisbursementStatus.Pending);
        dto.Amount.Should().Be(100_000m);
        loan.Status.Should().Be(LoanStatus.DisbursementPending);
        h.Audit.Events.Should().Contain(e => e.Action == "Disbursement.Initiated");
    }

    [Fact]
    public async Task Only_the_fund_originator_can_cancel_a_disbursement()
    {
        var h = new Harness();
        var loan = h.AddApprovedLoan();
        await h.AddVerifiedRecipientAsync();
        await h.InitiateAsync(OriginatorId, loan.Id);

        var act = () => h.Service.CancelDisbursementForLoanAsync(CoGuarantorId, loan.Id);

        await act.Should().ThrowAsync<InvalidPaymentException>()
            .WithMessage("*Only the Fund Originator*");
    }
}
