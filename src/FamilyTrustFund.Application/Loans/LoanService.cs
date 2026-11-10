using FamilyTrustFund.Application.Audit;
using FamilyTrustFund.Application.Contributions;
using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Application.Membership;
using FamilyTrustFund.Domain.Financial;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Membership;
using FamilyTrustFund.Domain.Repayments;

namespace FamilyTrustFund.Application.Loans;

/// <summary>
/// Application service for loan request and approval workflows. All financial
/// calculations and eligibility checks are enforced server-side; clients only
/// supply intent.
/// </summary>
/// <remarks>
/// A Group Guarantor fund requires a unanimous decision from every active
/// Guarantor before a loan is approved (ADR-052). Each Guarantor's decision is
/// recorded on its own <see cref="LoanApproval"/> row, so it is attributable and
/// a duplicate decision is impossible. The Fund Originator agrees the terms
/// before (or at) the first approval; after that the terms are frozen. Any
/// rejection rejects the whole request immediately.
/// </remarks>
public class LoanService
{
    private readonly ILoanRepository _loanRepository;
    private readonly IFundRepository _fundRepository;
    private readonly IMembershipRepository _membershipRepository;
    private readonly IContributionRepository _contributionRepository;
    private readonly IFundAccessService _fundAccess;
    private readonly IAuditLog _auditLog;
    private readonly GuarantorNotificationService _notifications;

    public LoanService(
        ILoanRepository loanRepository,
        IFundRepository fundRepository,
        IMembershipRepository membershipRepository,
        IContributionRepository contributionRepository,
        IFundAccessService fundAccess,
        IAuditLog auditLog,
        GuarantorNotificationService notifications)
    {
        _loanRepository = loanRepository;
        _fundRepository = fundRepository;
        _membershipRepository = membershipRepository;
        _contributionRepository = contributionRepository;
        _fundAccess = fundAccess;
        _auditLog = auditLog;
        _notifications = notifications;
    }

    /// <summary>
    /// Member requests a loan from one of their funds. The server validates:
    /// <list type="bullet">
    ///   <item>The fund exists and is active.</item>
    ///   <item>The caller is an active member of the fund.</item>
    ///   <item>The member does not already have an active loan (Pending, Approved or Disbursed) in any fund.</item>
    ///   <item>The member does not already have a pending request in this fund.</item>
    ///   <item>For Family funds, the member holds a contribution (Fund Credit) in the fund.</item>
    ///   <item>The requested amount is within the member's borrowing entitlement (Family) or fund capacity.</item>
    ///   <item>The fund has sufficient available lending capacity.</item>
    /// </list>
    /// </summary>
    public async Task<LoanDto> RequestLoanAsync(
        Guid memberId,
        RequestLoanRequest request,
        CancellationToken ct = default)
    {
        var fund = await _fundRepository.GetByIdAsync(request.FundId, ct)
            ?? throw new InvalidLoanException("Fund not found.");

        if (fund.Status != FundStatus.Active)
        {
            throw new InvalidLoanException("That fund is not currently active.");
        }

        var membership = await _membershipRepository.GetByFundAndMemberAsync(fund.Id, memberId, ct);
        if (membership is null || membership.Status != MemberStatus.Active)
        {
            throw new InvalidLoanException("You are not an active member of that fund.");
        }

        // A member can only hold one active loan at a time, regardless of which
        // of their funds it is in. They may not request another until it is
        // completed.
        var activeCount = await _loanRepository.CountActiveByMemberAsync(memberId, ct);
        if (activeCount > 0)
        {
            throw new InvalidLoanException("You already have an active loan. Complete it before requesting another.");
        }

        // One pending request at a time per member per fund.
        if (await _loanRepository.HasPendingRequestAsync(memberId, fund.Id, ct))
        {
            throw new InvalidLoanException("You already have a pending loan request in this fund.");
        }

        // Determine interest rate and funding source.
        decimal interestRate;
        LoanFundingSource fundingSource;
        decimal? borrowingEntitlement = null;

        if (fund.Type == FundType.Family)
        {
            // Family funds have no interest.
            interestRate = 0m;

            // A member must hold a contribution (Fund Credit) in the Family fund
            // before they can borrow from it.
            var fundCredit = await _contributionRepository.GetConfirmedFundCreditAsync(memberId, fund.Id, ct);
            if (fundCredit <= 0)
            {
                throw new InvalidLoanException("You need to contribute to this Family fund before requesting a loan.");
            }

            // Family borrowing entitlement = Fund Credit × multiplier.
            borrowingEntitlement = FundFinancialRules.FamilyBorrowingEntitlement(
                fundCredit, fund.ContributionMultiplier);

            // Funding source depends on transition state.
            fundingSource = fund.Status == FundStatus.Transitioned
                ? LoanFundingSource.FamilyCapital
                : LoanFundingSource.GuarantorCapital;
        }
        else
        {
            // External funds use the fund's configured interest rate.
            interestRate = fund.InterestRate ?? 0m;
            fundingSource = LoanFundingSource.GuarantorCapital;
        }

        if (!Enum.IsDefined(request.Frequency))
        {
            throw new InvalidLoanException("Select a repayment frequency.");
        }

        // Term is captured at request time; the instalment count must be
        // consistent with the term and frequency (LoanTermRules).
        var termMonths = request.TermMonths > 0 ? request.TermMonths : fund.MinLoanTermMonths;
        var instalments = request.Instalments > 0
            ? request.Instalments
            : LoanTermRules.ExpectedInstalments(termMonths, request.Frequency);

        var termErrors = LoanTermRules.Validate(
            termMonths, instalments, request.Frequency, fund.MinLoanTermMonths, fund.MaxLoanTermMonths);
        if (termErrors.Count > 0)
        {
            throw new InvalidLoanException(string.Join(" ", termErrors));
        }

        // Server-authoritative available lending capacity.
        var activeDisbursedTotal = await _loanRepository.SumActiveDisbursedByFundAsync(fund.Id, ct);
        var availableCapacity = FundFinancialRules.AvailableLendingCapacity(fund, activeDisbursedTotal);

        if (request.Amount > availableCapacity)
        {
            throw new InvalidLoanException(
                $"Requested amount exceeds available lending capacity. Available: ₦{availableCapacity:N2}.");
        }

        if (borrowingEntitlement.HasValue && request.Amount > borrowingEntitlement.Value)
        {
            throw new InvalidLoanException(
                $"Requested amount exceeds your borrowing entitlement of ₦{borrowingEntitlement.Value:N2}.");
        }

        // Create the loan request.
        var loan = Loan.Request(
            fund.Id,
            memberId,
            request.Amount,
            request.Frequency,
            interestRate,
            fundingSource,
            request.Purpose,
            termMonths,
            instalments);

        // The approval snapshot is fixed at request time: one required decision
        // per active Guarantor. A single-Guarantor fund gets exactly one row, so
        // both fund modes share the same decision code path (ADR-052).
        var approverIds = await _fundAccess.GetRequiredApproverIdsAsync(fund, ct);
        if (approverIds.Count == 0)
        {
            throw new InvalidLoanException("This fund has no Guarantor available to approve loans.");
        }

        _loanRepository.Add(loan);
        _loanRepository.AddApprovals(approverIds.Select(id => LoanApproval.Require(loan.Id, id)));

        await _auditLog.RecordAsync(memberId, "Loan.Requested", "Loan", loan.Id,
            $"Amount={request.Amount}, Frequency={request.Frequency}, TermMonths={termMonths}, Instalments={instalments}, " +
            $"RequiredApprovals={approverIds.Count}", ct);
        await _loanRepository.SaveChangesAsync(ct);

        // Only group funds notify every approver; single-Guarantor funds keep
        // their existing behaviour.
        if (fund.IsGroupFund)
        {
            foreach (var approverId in approverIds)
            {
                await _notifications.LoanAwaitingDecisionAsync(fund, approverId, request.Amount, ct);
            }
        }

        return await ToLoanDtoAsync(loan, fund, ct);
    }

    /// <summary>
    /// Previews a member's proposed loan terms without creating anything. The
    /// server calculates the repayment breakdown; the client only renders it.
    /// Validation problems are returned as <see cref="LoanPreviewDto.Errors"/>
    /// rather than thrown so the form can display them inline.
    /// </summary>
    public async Task<LoanPreviewDto> PreviewLoanAsync(
        Guid memberId,
        LoanPreviewRequest request,
        CancellationToken ct = default)
    {
        var fund = await _fundRepository.GetByIdAsync(request.FundId, ct)
            ?? throw new InvalidLoanException("Fund not found.");

        if (fund.Status != FundStatus.Active)
        {
            throw new InvalidLoanException("That fund is not currently active.");
        }

        var membership = await _membershipRepository.GetByFundAndMemberAsync(fund.Id, memberId, ct);
        if (membership is null || membership.Status != MemberStatus.Active)
        {
            throw new InvalidLoanException("You are not an active member of that fund.");
        }

        var interestRate = fund.Type == FundType.Family ? 0m : fund.InterestRate ?? 0m;

        var activeDisbursedTotal = await _loanRepository.SumActiveDisbursedByFundAsync(fund.Id, ct);
        var availableCapacity = FundFinancialRules.AvailableLendingCapacity(fund, activeDisbursedTotal);

        decimal? fundCredit = null;
        decimal? borrowingEntitlement = null;
        if (fund.Type == FundType.Family)
        {
            fundCredit = await _contributionRepository.GetConfirmedFundCreditAsync(memberId, fund.Id, ct);
            borrowingEntitlement = FundFinancialRules.FamilyBorrowingEntitlement(
                fundCredit.Value, fund.ContributionMultiplier);
        }

        return BuildPreview(
            fund,
            request.Amount,
            request.Frequency,
            request.TermMonths,
            request.Instalments,
            interestRate,
            activeDisbursedTotal,
            availableCapacity,
            fundCredit,
            borrowingEntitlement);
    }

    /// <summary>
    /// Previews the Guarantor's proposed approval terms against the member's
    /// original request, including the list of terms that changed. Once the
    /// group has frozen the terms, the frozen terms are previewed instead.
    /// </summary>
    public async Task<GuarantorLoanPreviewDto> PreviewApprovalAsync(
        Guid guarantorId,
        GuarantorLoanPreviewRequest request,
        CancellationToken ct = default)
    {
        var loan = await _loanRepository.GetByIdAsync(request.LoanId, ct)
            ?? throw new InvalidLoanException("Loan not found.");

        if (loan.Status != LoanStatus.Pending)
        {
            throw new InvalidLoanException("Only pending loans can be approved.");
        }

        var fund = await _fundRepository.GetByIdAsync(loan.FundId, ct)
            ?? throw new InvalidLoanException("Fund not found.");

        if (!await _fundAccess.CanAccessFundAsync(fund, guarantorId, ct))
        {
            throw new InvalidLoanException("You do not have permission to approve this loan.");
        }

        decimal approvedAmount;
        RepaymentFrequency frequency;
        int termMonths;
        int instalments;

        if (loan.HasFrozenTerms)
        {
            approvedAmount = loan.ApprovedAmount ?? loan.RequestedAmount;
            frequency = loan.ApprovedFrequency ?? loan.RequestedFrequency;
            termMonths = loan.ApprovedTermMonths ?? loan.RequestedTermMonths ?? fund.MinLoanTermMonths;
            instalments = loan.RepaymentTerm;
        }
        else
        {
            approvedAmount = request.ApprovedAmount > 0 ? request.ApprovedAmount : loan.RequestedAmount;
            (frequency, termMonths, instalments) = ResolveApprovedTerms(
                loan, fund, request.ApprovedFrequency, request.ApprovedTermMonths, request.ApprovedInstalments);

            // Only the Fund Originator may change terms. A co-guarantor preview
            // must therefore show the member's requested terms.
            if (fund.GuarantorId != guarantorId)
            {
                var requestedTerms = ResolveApprovedTerms(
                    loan, fund, loan.RequestedFrequency, loan.RequestedTermMonths ?? 0, loan.RequestedInstalments ?? 0);
                frequency = requestedTerms.Frequency;
                termMonths = requestedTerms.TermMonths;
                instalments = requestedTerms.Instalments;
            }
        }

        var activeDisbursedTotal = await _loanRepository.SumActiveDisbursedByFundAsync(fund.Id, ct);
        var availableCapacity = FundFinancialRules.AvailableLendingCapacity(fund, activeDisbursedTotal);

        decimal? fundCredit = null;
        decimal? borrowingEntitlement = null;
        if (fund.Type == FundType.Family)
        {
            fundCredit = await _contributionRepository.GetConfirmedFundCreditAsync(loan.MemberId, fund.Id, ct);
            borrowingEntitlement = FundFinancialRules.FamilyBorrowingEntitlement(
                fundCredit.Value, fund.ContributionMultiplier);
        }

        var preview = BuildPreview(
            fund,
            approvedAmount,
            frequency,
            termMonths,
            instalments,
            loan.InterestRate,
            activeDisbursedTotal,
            availableCapacity,
            fundCredit,
            borrowingEntitlement);

        var changes = loan.HasFrozenTerms
            ? Array.Empty<LoanTermChangeDto>()
            : BuildChanges(loan, approvedAmount, frequency, termMonths, instalments);

        return new GuarantorLoanPreviewDto
        {
            Preview = preview,
            TermsChanged = changes.Count > 0,
            Changes = changes,
        };
    }

    /// <summary>
    /// Guarantor records their decision on a pending loan request. The loan is
    /// only approved once every required Guarantor has approved; any rejection
    /// rejects the request immediately (ADR-052).
    /// </summary>
    public async Task<LoanDto> ApproveLoanAsync(
        Guid guarantorId,
        ApproveLoanRequest request,
        CancellationToken ct = default)
    {
        var loan = await _loanRepository.GetByIdAsync(request.LoanId, ct)
            ?? throw new InvalidLoanException("Loan not found.");

        if (loan.Status != LoanStatus.Pending)
        {
            throw new InvalidLoanException("Only pending loans can be approved.");
        }

        var fund = await _fundRepository.GetByIdAsync(loan.FundId, ct)
            ?? throw new InvalidLoanException("Fund not found.");

        if (!await _fundAccess.CanAccessFundAsync(fund, guarantorId, ct))
        {
            throw new InvalidLoanException("You do not have permission to approve this loan.");
        }

        var approval = await GetApprovalForDecisionAsync(loan, fund, guarantorId, ct);
        if (!approval.IsPending)
        {
            throw new InvalidLoanException("You have already recorded a decision on this loan.");
        }

        if (loan.HasFrozenTerms)
        {
            // Terms are already agreed: this Guarantor is simply voting yes.
        }
        else
        {
            var isOriginator = fund.GuarantorId == guarantorId;
            var approvedAmount = request.ApprovedAmount > 0 ? request.ApprovedAmount : loan.RequestedAmount;
            var (frequency, termMonths, instalments) = ResolveApprovedTerms(
                loan, fund, request.ApprovedFrequency, request.ApprovedTermMonths, request.ApprovedInstalments);

            var changes = BuildChanges(loan, approvedAmount, frequency, termMonths, instalments);
            if (!isOriginator && changes.Count > 0)
            {
                throw new InvalidLoanException(
                    "Only the Fund Originator can change the loan terms. Approve the member's requested terms as they stand.");
            }

            var termErrors = LoanTermRules.Validate(
                termMonths, instalments, frequency, fund.MinLoanTermMonths, fund.MaxLoanTermMonths);
            if (termErrors.Count > 0)
            {
                throw new InvalidLoanException(string.Join(" ", termErrors));
            }

            await EnsureApprovalWithinLimitsAsync(fund, loan, approvedAmount, ct);

            // Any change from the member's request must be acknowledged, because
            // the terms are about to become immutable.
            if (changes.Count > 0 && !request.ConfirmedWithMember)
            {
                throw new InvalidLoanException(
                    "Approved terms differ from the member's request. Confirm the new arrangement with the borrower and acknowledge it before approving.");
            }

            var totalRepayable = CalculateTotalRepayable(approvedAmount, loan.InterestRate);

            // The originator owns the agreed terms; a co-guarantor approving the
            // member's request freezes the originator's request terms.
            loan.FreezeApprovedTerms(
                isOriginator ? guarantorId : fund.GuarantorId,
                approvedAmount,
                frequency,
                totalRepayable,
                instalments,
                termMonths);
        }

        approval.Approve(request.Comment);

        try
        {
            await _loanRepository.SaveChangesAsync(ct);
        }
        catch (LoanDecisionConflictException)
        {
            // Another Guarantor froze the terms at the same moment. The caller
            // reloads and decides against the now-frozen terms.
            throw new InvalidLoanException(
                "Another Guarantor updated this loan at the same time. Reload the loan and try again.");
        }

        var approvals = await _loanRepository.GetApprovalsAsync(loan.Id, ct);
        var finalisedNow = false;

        if (loan.Status == LoanStatus.Pending && approvals.Count > 0 && approvals.All(a => a.IsApproved))
        {
            loan.CompleteUnanimousApproval();

            try
            {
                await _loanRepository.SaveChangesAsync(ct);
                finalisedNow = true;
            }
            catch (LoanDecisionConflictException)
            {
                // Another Guarantor completed the unanimous approval. The loan is
                // already approved; return the committed state (idempotent).
            }
        }

        await _auditLog.RecordAsync(guarantorId, "Loan.ApprovalRecorded", "Loan", loan.Id,
            $"ApprovedAmount={loan.ApprovedAmount}, ApprovedFrequency={loan.ApprovedFrequency}, " +
            $"ApprovedTermMonths={loan.ApprovedTermMonths}, ApprovedInstalments={loan.RepaymentTerm}, " +
            $"ConfirmedWithMember={request.ConfirmedWithMember}", ct);

        var finalLoan = await _loanRepository.GetByIdAsNoTrackingAsync(loan.Id, ct) ?? loan;

        if (finalisedNow && finalLoan.Status == LoanStatus.Approved)
        {
            await _auditLog.RecordAsync(guarantorId, "Loan.Approved", "Loan", finalLoan.Id,
                $"ApprovedAmount={finalLoan.ApprovedAmount}, TotalRepayable={finalLoan.TotalRepayable}, " +
                $"RequiredApprovals={approvals.Count}", ct);

            await _notifications.LoanApprovedAsync(
                fund,
                finalLoan.MemberId,
                finalLoan.ApprovedAmount ?? finalLoan.RequestedAmount,
                ct);
        }

        return await ToLoanDtoAsync(finalLoan, fund, ct);
    }

    /// <summary>
    /// Guarantor rejects a pending loan request. In a group fund any single
    /// rejection resolves the request immediately and closes the other
    /// Guarantors' outstanding decisions.
    /// </summary>
    public async Task<LoanDto> RejectLoanAsync(
        Guid guarantorId,
        RejectLoanRequest request,
        CancellationToken ct = default)
    {
        var loan = await _loanRepository.GetByIdAsync(request.LoanId, ct)
            ?? throw new InvalidLoanException("Loan not found.");

        if (loan.Status != LoanStatus.Pending)
        {
            throw new InvalidLoanException("Only pending loans can be rejected.");
        }

        var fund = await _fundRepository.GetByIdAsync(loan.FundId, ct)
            ?? throw new InvalidLoanException("Fund not found.");

        if (!await _fundAccess.CanAccessFundAsync(fund, guarantorId, ct))
        {
            throw new InvalidLoanException("You do not have permission to reject this loan.");
        }

        var approval = await GetApprovalForDecisionAsync(loan, fund, guarantorId, ct);
        if (!approval.IsPending)
        {
            throw new InvalidLoanException("You have already recorded a decision on this loan.");
        }

        approval.Reject(request.Reason);
        loan.Reject(guarantorId, request.Reason);

        // Close every other outstanding decision: the request is resolved.
        var approvals = await _loanRepository.GetApprovalsAsync(loan.Id, ct);
        foreach (var other in approvals.Where(a => a.Id != approval.Id && a.IsPending))
        {
            other.Cancel();
        }

        await _auditLog.RecordAsync(guarantorId, "Loan.Rejected", "Loan", loan.Id,
            request.Reason is not null ? $"Reason={request.Reason}" : null, ct);
        await _loanRepository.SaveChangesAsync(ct);

        await _notifications.LoanRejectedAsync(fund, loan.MemberId, request.Reason, ct);

        return await ToLoanDtoAsync(loan, fund, ct);
    }

    /// <summary>
    /// Member cancels their own pending loan request. Only a Pending request can
    /// be withdrawn by the member; approved or disbursed loans are not cancellable
    /// by the member.
    /// </summary>
    public async Task<LoanDto> CancelLoanByMemberAsync(
        Guid memberId,
        Guid loanId,
        CancellationToken ct = default)
    {
        var loan = await _loanRepository.GetByIdAsync(loanId, ct)
            ?? throw new InvalidLoanException("Loan not found.");

        if (loan.MemberId != memberId)
        {
            throw new InvalidLoanException("You can only cancel your own loan request.");
        }

        var fund = await _fundRepository.GetByIdAsync(loan.FundId, ct)
            ?? throw new InvalidLoanException("Fund not found.");

        loan.CancelByMember(memberId);
        await CancelOutstandingApprovalsAsync(loan.Id, ct);

        await _auditLog.RecordAsync(memberId, "Loan.Cancelled", "Loan", loan.Id,
            "Cancelled pending loan request", ct);
        await _loanRepository.SaveChangesAsync(ct);

        return await ToLoanDtoAsync(loan, fund, ct);
    }

    /// <summary>
    /// Fund Originator cancels a pending, approved, or disbursement-pending (not
    /// yet disbursed) loan in their fund. Disbursed and completed loans can never
    /// be cancelled. Only the Fund Originator may cancel, even in a group fund
    /// (ADR-051).
    /// </summary>
    public async Task<LoanDto> CancelLoanByGuarantorAsync(
        Guid guarantorId,
        Guid loanId,
        CancellationToken ct = default)
    {
        var loan = await _loanRepository.GetByIdAsync(loanId, ct)
            ?? throw new InvalidLoanException("Loan not found.");

        var fund = await _fundRepository.GetByIdAsync(loan.FundId, ct)
            ?? throw new InvalidLoanException("Fund not found.");

        if (fund.GuarantorId != guarantorId)
        {
            throw new InvalidLoanException(
                fund.IsGroupFund
                    ? "Only the Fund Originator can cancel a loan."
                    : "You do not have permission to cancel this loan.");
        }

        loan.CancelByGuarantor(guarantorId);
        await CancelOutstandingApprovalsAsync(loan.Id, ct);

        await _auditLog.RecordAsync(guarantorId, "Loan.Cancelled", "Loan", loan.Id,
            "Guarantor cancelled loan before disbursement", ct);
        await _loanRepository.SaveChangesAsync(ct);

        return await ToLoanDtoAsync(loan, fund, ct);
    }

    /// <summary>
    /// Returns pending loan requests for the Guarantor's review queue. A Group
    /// Guarantor only sees loans where their own decision is still outstanding.
    /// </summary>
    public async Task<IReadOnlyList<LoanDto>> GetPendingRequestsForGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default)
    {
        var items = await _loanRepository.GetPendingForGuarantorAsync(guarantorId, ct);
        return await ToLoanDtosAsync(items, ct);
    }

    /// <summary>
    /// Returns the pending loan requests for a fund. Ownership is enforced by the caller.
    /// </summary>
    public async Task<IReadOnlyList<LoanDto>> GetLoansByFundAsync(
        Guid fundId,
        CancellationToken ct = default)
    {
        var items = await _loanRepository.GetByFundAsync(fundId, ct);
        return await ToLoanDtosAsync(items, ct);
    }

    /// <summary>
    /// Returns a single loan only if the specified Guarantor may operate the
    /// loan's fund (originator or active co-guarantor). Returns null otherwise.
    /// </summary>
    public async Task<LoanDto?> GetLoanByIdForGuarantorAsync(
        Guid guarantorId,
        Guid loanId,
        CancellationToken ct = default)
    {
        var loan = await _loanRepository.GetByIdAsync(loanId, ct);
        if (loan is null)
        {
            return null;
        }

        var fund = await _fundRepository.GetByIdAsync(loan.FundId, ct);
        if (fund is null || !await _fundAccess.CanAccessFundAsync(fund, guarantorId, ct))
        {
            return null;
        }

        return await ToLoanDtoAsync(loan, fund, ct);
    }

    /// <summary>
    /// Returns all loans for a member across all their funds.
    /// </summary>
    public async Task<IReadOnlyList<LoanDto>> GetLoansByMemberAsync(
        Guid memberId,
        CancellationToken ct = default)
    {
        var items = await _loanRepository.GetByMemberAsync(memberId, ct);
        return await ToLoanDtosAsync(items, ct);
    }

    /// <summary>
    /// Returns the lending capacity for a fund and optionally a member's
    /// borrowing entitlement (Family funds only).
    /// </summary>
    public async Task<LendingCapacityDto> GetLendingCapacityAsync(
        Guid fundId,
        Guid? memberId = null,
        CancellationToken ct = default)
    {
        var fund = await _fundRepository.GetByIdAsync(fundId, ct)
            ?? throw new InvalidLoanException("Fund not found.");

        var activeDisbursedTotal = await _loanRepository.SumActiveDisbursedByFundAsync(fund.Id, ct);
        var availableCapacity = FundFinancialRules.AvailableLendingCapacity(fund, activeDisbursedTotal);

        decimal? borrowingEntitlement = null;
        decimal? fundCredit = null;

        // Family borrowing entitlement is based on the member's confirmed Fund
        // Credit (contributions), derived from authoritative records.
        if (fund.Type == FundType.Family && memberId.HasValue)
        {
            fundCredit = await _contributionRepository.GetConfirmedFundCreditAsync(memberId.Value, fund.Id, ct);
            borrowingEntitlement = FundFinancialRules.FamilyBorrowingEntitlement(
                fundCredit.Value, fund.ContributionMultiplier);
        }

        return new LendingCapacityDto
        {
            FundId = fund.Id,
            CommittedCapital = fund.CommittedCapital,
            ActiveDisbursedLoans = activeDisbursedTotal,
            AvailableLendingCapacity = availableCapacity,
            FamilyBorrowingEntitlement = borrowingEntitlement,
            FundCredit = fundCredit,
        };
    }

    /// <summary>
    /// Calculates the total repayable amount including interest.
    /// For Family funds (interest = 0), total repayable equals the principal.
    /// For External funds, interest is calculated on the approved amount.
    /// </summary>
    /// <remarks>
    /// This is a simplified interest calculation for the MVP. A more sophisticated
    /// amortisation model can be added later without changing the domain contract.
    /// </remarks>
    public static decimal CalculateTotalRepayable(decimal principal, decimal interestRate)
    {
        if (principal <= 0)
        {
            throw new InvalidLoanException("Principal must be greater than zero.");
        }

        if (interestRate < 0)
        {
            throw new InvalidLoanException("Interest rate cannot be negative.");
        }

        if (interestRate == 0)
        {
            return principal;
        }

        // Simple interest: principal + (principal × rate / 100)
        var interest = Math.Round(principal * interestRate / 100m, 2, MidpointRounding.AwayFromZero);
        return principal + interest;
    }

    /// <summary>
    /// Loads the caller's approval row, creating a fallback row for a legacy
    /// single-Guarantor loan that predates the approval snapshot. Group funds
    /// always have their rows, so a missing row is a permission failure.
    /// </summary>
    private async Task<LoanApproval> GetApprovalForDecisionAsync(
        Loan loan,
        Fund fund,
        Guid guarantorId,
        CancellationToken ct)
    {
        var approval = await _loanRepository.GetApprovalAsync(loan.Id, guarantorId, ct);
        if (approval is not null)
        {
            return approval;
        }

        if (fund.IsGroupFund || fund.GuarantorId != guarantorId)
        {
            throw new InvalidLoanException("You are not a required approver for this loan.");
        }

        approval = LoanApproval.Require(loan.Id, guarantorId);
        _loanRepository.AddApproval(approval);
        return approval;
    }

    private async Task CancelOutstandingApprovalsAsync(Guid loanId, CancellationToken ct)
    {
        var approvals = await _loanRepository.GetApprovalsAsync(loanId, ct);
        foreach (var approval in approvals.Where(a => a.IsPending))
        {
            approval.Cancel();
        }
    }

    private async Task EnsureApprovalWithinLimitsAsync(
        Fund fund,
        Loan loan,
        decimal approvedAmount,
        CancellationToken ct)
    {
        // Ensure approved amount is within available lending capacity.
        var activeDisbursedTotal = await _loanRepository.SumActiveDisbursedByFundAsync(fund.Id, ct);
        var availableCapacity = FundFinancialRules.AvailableLendingCapacity(fund, activeDisbursedTotal);

        if (approvedAmount > availableCapacity)
        {
            throw new InvalidLoanException(
                $"Approved amount exceeds available lending capacity. Available: ₦{availableCapacity:N2}.");
        }

        // Family borrowing entitlement is enforced at approval too, because the
        // Guarantor may change the amount.
        if (fund.Type == FundType.Family)
        {
            var fundCredit = await _contributionRepository.GetConfirmedFundCreditAsync(loan.MemberId, fund.Id, ct);
            var entitlement = FundFinancialRules.FamilyBorrowingEntitlement(
                fundCredit, fund.ContributionMultiplier);

            if (approvedAmount > entitlement)
            {
                throw new InvalidLoanException(
                    $"Approved amount exceeds the member's borrowing entitlement of ₦{entitlement:N2}.");
            }
        }
    }

    /// <summary>
    /// Resolves the effective approved terms, falling back to the member's
    /// request (or the fund's minimum term) when the Guarantor does not supply a
    /// value. Frequency changes derive a fresh instalment count because the
    /// requested count belongs to the old frequency.
    /// </summary>
    private static (RepaymentFrequency Frequency, int TermMonths, int Instalments) ResolveApprovedTerms(
        Loan loan,
        Fund fund,
        RepaymentFrequency approvedFrequency,
        int approvedTermMonths,
        int approvedInstalments)
    {
        var frequency = approvedFrequency != 0
            ? approvedFrequency
            : loan.RequestedFrequency != 0
                ? loan.RequestedFrequency
                : RepaymentFrequency.Monthly;

        if (!Enum.IsDefined(frequency))
        {
            throw new InvalidLoanException("Select a repayment frequency.");
        }

        var termMonths = approvedTermMonths > 0
            ? approvedTermMonths
            : loan.RequestedTermMonths is > 0
                ? loan.RequestedTermMonths.Value
                : fund.MinLoanTermMonths;

        int instalments;
        if (approvedInstalments > 0)
        {
            instalments = approvedInstalments;
        }
        else if (frequency == loan.RequestedFrequency && loan.RequestedInstalments is > 0)
        {
            instalments = loan.RequestedInstalments.Value;
        }
        else
        {
            instalments = LoanTermRules.ExpectedInstalments(termMonths, frequency);
        }

        return (frequency, termMonths, instalments);
    }

    /// <summary>
    /// Builds the server-authoritative repayment preview. Invalid input is
    /// reported through <see cref="LoanPreviewDto.Errors"/> instead of throwing
    /// so the client can render inline validation.
    /// </summary>
    private static LoanPreviewDto BuildPreview(
        Fund fund,
        decimal amount,
        RepaymentFrequency frequency,
        int termMonths,
        int instalments,
        decimal interestRate,
        decimal activeDisbursedTotal,
        decimal availableCapacity,
        decimal? fundCredit,
        decimal? borrowingEntitlement)
    {
        var errors = new List<string>();

        if (amount <= 0)
        {
            errors.Add("Loan amount must be greater than zero.");
        }

        var frequencyValid = Enum.IsDefined(frequency);
        if (!frequencyValid)
        {
            errors.Add("Select a repayment frequency.");
        }

        var termErrors = new List<string>();

        if (frequencyValid)
        {
            termErrors.AddRange(LoanTermRules.Validate(
                termMonths, instalments, frequency, fund.MinLoanTermMonths, fund.MaxLoanTermMonths));
        }
        else if (termMonths <= 0)
        {
            termErrors.Add("Loan term must be greater than zero months.");
        }

        errors.AddRange(termErrors);

        if (amount > availableCapacity)
        {
            errors.Add(
                $"Requested amount exceeds the fund's available lending capacity of ₦{availableCapacity:N2}.");
        }

        if (borrowingEntitlement.HasValue && amount > borrowingEntitlement.Value)
        {
            errors.Add(
                $"Requested amount exceeds your borrowing entitlement of ₦{borrowingEntitlement.Value:N2} " +
                $"(Fund Credit ₦{(fundCredit ?? 0m):N2} × {fund.ContributionMultiplier:0.##}).");
        }

        var totalRepayable = amount > 0 ? CalculateTotalRepayable(amount, interestRate) : 0m;
        var interestAmount = totalRepayable > amount ? totalRepayable - amount : 0m;

        decimal instalmentAmount = 0m;
        decimal finalInstalmentAmount = 0m;
        DateTime? firstDueDate = null;
        DateTime? finalDueDate = null;

        // Only build a schedule when the terms themselves are valid. This keeps
        // an invalid (possibly malicious) instalment count from allocating a
        // huge schedule just to produce an error message.
        if (amount > 0 && frequencyValid && termErrors.Count == 0 && instalments > 0)
        {
            // Dates are indicative until disbursement, which fixes the start
            // date. The amounts are exact for the chosen terms.
            var schedule = RepaymentRules.BuildV1Instalments(
                Guid.Empty, amount, totalRepayable, frequency, instalments, DateTime.UtcNow);

            if (schedule.Count > 0)
            {
                instalmentAmount = schedule[0].ExpectedAmount;
                finalInstalmentAmount = schedule[^1].ExpectedAmount;
                firstDueDate = schedule[0].DueDateUtc;
                finalDueDate = schedule[^1].DueDateUtc;
            }
        }

        return new LoanPreviewDto
        {
            FundId = fund.Id,
            FundType = fund.Type,
            Amount = amount,
            Frequency = frequency,
            TermMonths = termMonths,
            Instalments = instalments,
            InterestRate = interestRate,
            TotalRepayable = totalRepayable,
            InterestAmount = interestAmount,
            InstalmentAmount = instalmentAmount,
            FinalInstalmentAmount = finalInstalmentAmount,
            FirstDueDateUtc = firstDueDate,
            FinalDueDateUtc = finalDueDate,
            CommittedCapital = fund.CommittedCapital,
            ActiveDisbursedLoans = activeDisbursedTotal,
            AvailableLendingCapacity = availableCapacity,
            FamilyBorrowingEntitlement = borrowingEntitlement,
            FundCredit = fundCredit,
            MinTermMonths = fund.MinLoanTermMonths,
            MaxTermMonths = fund.MaxLoanTermMonths,
            IsValid = errors.Count == 0,
            Errors = errors,
        };
    }

    /// <summary>
    /// Compares the Guarantor's proposed terms with the member's original
    /// request and returns only the terms that changed.
    /// </summary>
    private static IReadOnlyList<LoanTermChangeDto> BuildChanges(
        Loan loan,
        decimal approvedAmount,
        RepaymentFrequency frequency,
        int termMonths,
        int instalments)
    {
        var changes = new List<LoanTermChangeDto>();

        if (approvedAmount != loan.RequestedAmount)
        {
            changes.Add(new LoanTermChangeDto
            {
                Field = "Amount",
                Requested = $"₦{loan.RequestedAmount:N2}",
                Approved = $"₦{approvedAmount:N2}",
            });
        }

        if (frequency != loan.RequestedFrequency)
        {
            changes.Add(new LoanTermChangeDto
            {
                Field = "Frequency",
                Requested = loan.RequestedFrequency.ToString(),
                Approved = frequency.ToString(),
            });
        }

        if (loan.RequestedTermMonths is > 0 && termMonths != loan.RequestedTermMonths.Value)
        {
            changes.Add(new LoanTermChangeDto
            {
                Field = "Term",
                Requested = $"{loan.RequestedTermMonths} month(s)",
                Approved = $"{termMonths} month(s)",
            });
        }

        if (loan.RequestedInstalments is > 0 && instalments != loan.RequestedInstalments.Value)
        {
            changes.Add(new LoanTermChangeDto
            {
                Field = "Instalments",
                Requested = $"{loan.RequestedInstalments}",
                Approved = $"{instalments}",
            });
        }

        return changes;
    }

    private async Task<LoanDto> ToLoanDtoAsync(Loan loan, Fund fund, CancellationToken ct)
    {
        var approvals = await _loanRepository.GetApprovalsWithDetailsAsync(new[] { loan.Id }, ct);
        return ToLoanDto(
            loan,
            fund.Name,
            fund.Type,
            string.Empty,
            string.Empty,
            fund.GuarantorMode,
            fund.GuarantorId,
            approvals);
    }

    private async Task<IReadOnlyList<LoanDto>> ToLoanDtosAsync(
        IReadOnlyList<LoanWithDetails> items,
        CancellationToken ct)
    {
        if (items.Count == 0)
        {
            return Array.Empty<LoanDto>();
        }

        var approvals = await _loanRepository.GetApprovalsWithDetailsAsync(
            items.Select(i => i.Loan.Id).Distinct().ToList(),
            ct);

        var byLoan = approvals
            .GroupBy(a => a.Approval.LoanId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LoanApprovalWithDetails>)g.ToList());

        return items.Select(i => ToLoanDto(
            i.Loan,
            i.FundName,
            i.FundType,
            i.MemberDisplayName,
            i.MemberEmail,
            i.GuarantorMode,
            i.GuarantorId,
            byLoan.GetValueOrDefault(i.Loan.Id) ?? Array.Empty<LoanApprovalWithDetails>())).ToList();
    }

    private static LoanDto ToLoanDto(
        Loan loan,
        string fundName,
        FundType fundType,
        string memberDisplayName,
        string memberEmail,
        FundGuarantorMode guarantorMode,
        Guid fundOriginatorId,
        IReadOnlyList<LoanApprovalWithDetails> approvals)
    {
        var approvalDtos = approvals.Select(a => new LoanApprovalDto
        {
            GuarantorId = a.Approval.GuarantorId,
            GuarantorDisplayName = a.GuarantorDisplayName,
            GuarantorEmail = a.GuarantorEmail,
            Status = a.Approval.Status,
            DecidedAtUtc = a.Approval.DecidedAtUtc,
            Comment = a.Approval.Comment,
        }).ToList();

        return new LoanDto
        {
            Id = loan.Id,
            FundId = loan.FundId,
            FundName = fundName,
            FundType = fundType,
            MemberId = loan.MemberId,
            MemberDisplayName = memberDisplayName,
            MemberEmail = memberEmail,
            RequestedAmount = loan.RequestedAmount,
            ApprovedAmount = loan.ApprovedAmount,
            InterestRate = loan.InterestRate,
            RequestedFrequency = loan.RequestedFrequency,
            ApprovedFrequency = loan.ApprovedFrequency,
            RequestedTermMonths = loan.RequestedTermMonths,
            RequestedInstalments = loan.RequestedInstalments,
            ApprovedTermMonths = loan.ApprovedTermMonths,
            RepaymentTerm = loan.RepaymentTerm,
            Status = loan.Status,
            FundingSource = loan.FundingSource,
            OutstandingBalance = loan.OutstandingBalance,
            TotalRepayable = loan.TotalRepayable,
            Purpose = loan.Purpose,
            RejectionReason = loan.RejectionReason,
            RequestedAtUtc = loan.RequestedAtUtc,
            ApprovedAtUtc = loan.ApprovedAtUtc,
            RejectedAtUtc = loan.RejectedAtUtc,
            CancelledAtUtc = loan.CancelledAtUtc,
            FundGuarantorMode = guarantorMode,
            FundOriginatorId = fundOriginatorId,
            TermsFrozen = loan.HasFrozenTerms,
            RequiredApprovalCount = approvalDtos.Count,
            ApprovedApprovalCount = approvalDtos.Count(a => a.Status == LoanApprovalStatus.Approved),
            RejectedApprovalCount = approvalDtos.Count(a => a.Status == LoanApprovalStatus.Rejected),
            Approvals = approvalDtos,
        };
    }
}
