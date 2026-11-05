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
public class LoanService
{
    private readonly ILoanRepository _loanRepository;
    private readonly IFundRepository _fundRepository;
    private readonly IMembershipRepository _membershipRepository;
    private readonly IContributionRepository _contributionRepository;
    private readonly IAuditLog _auditLog;

    public LoanService(
        ILoanRepository loanRepository,
        IFundRepository fundRepository,
        IMembershipRepository membershipRepository,
        IContributionRepository contributionRepository,
        IAuditLog auditLog)
    {
        _loanRepository = loanRepository;
        _fundRepository = fundRepository;
        _membershipRepository = membershipRepository;
        _contributionRepository = contributionRepository;
        _auditLog = auditLog;
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

        _loanRepository.Add(loan);
        await _auditLog.RecordAsync(memberId, "Loan.Requested", "Loan", loan.Id,
            $"Amount={request.Amount}, Frequency={request.Frequency}, TermMonths={termMonths}, Instalments={instalments}", ct);
        await _loanRepository.SaveChangesAsync(ct);

        return ToLoanDto(loan, fund);
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
    /// original request, including the list of terms that changed.
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

        if (fund.GuarantorId != guarantorId)
        {
            throw new InvalidLoanException("You do not have permission to approve this loan.");
        }

        var (frequency, termMonths, instalments) = ResolveApprovedTerms(
            loan, fund, request.ApprovedFrequency, request.ApprovedTermMonths, request.ApprovedInstalments);

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
            request.ApprovedAmount,
            frequency,
            termMonths,
            instalments,
            loan.InterestRate,
            activeDisbursedTotal,
            availableCapacity,
            fundCredit,
            borrowingEntitlement);

        var changes = BuildChanges(loan, request.ApprovedAmount, frequency, termMonths, instalments);

        return new GuarantorLoanPreviewDto
        {
            Preview = preview,
            TermsChanged = changes.Count > 0,
            Changes = changes,
        };
    }

    /// <summary>
    /// Guarantor approves a pending loan request. The Guarantor may adjust the
    /// approved amount and repayment frequency. The total repayable is
    /// server-calculated based on the approved terms.
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

        if (fund.GuarantorId != guarantorId)
        {
            throw new InvalidLoanException("You do not have permission to approve this loan.");
        }

        var (frequency, termMonths, instalments) = ResolveApprovedTerms(
            loan, fund, request.ApprovedFrequency, request.ApprovedTermMonths, request.ApprovedInstalments);

        var termErrors = LoanTermRules.Validate(
            termMonths, instalments, frequency, fund.MinLoanTermMonths, fund.MaxLoanTermMonths);
        if (termErrors.Count > 0)
        {
            throw new InvalidLoanException(string.Join(" ", termErrors));
        }

        // Ensure approved amount is within available lending capacity.
        var activeDisbursedTotal = await _loanRepository.SumActiveDisbursedByFundAsync(fund.Id, ct);
        var availableCapacity = FundFinancialRules.AvailableLendingCapacity(fund, activeDisbursedTotal);

        if (request.ApprovedAmount > availableCapacity)
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

            if (request.ApprovedAmount > entitlement)
            {
                throw new InvalidLoanException(
                    $"Approved amount exceeds the member's borrowing entitlement of ₦{entitlement:N2}.");
            }
        }

        // Any change from the member's request must be acknowledged, because the
        // Guarantor is expected to have confirmed the new arrangement with them.
        var changes = BuildChanges(loan, request.ApprovedAmount, frequency, termMonths, instalments);
        if (changes.Count > 0 && !request.ConfirmedWithMember)
        {
            throw new InvalidLoanException(
                "Approved terms differ from the member's request. Confirm the new arrangement with the borrower and acknowledge it before approving.");
        }

        // Calculate total repayable: for Family funds, total repayable equals
        // approved amount (no interest). For External funds, total repayable
        // includes interest.
        var totalRepayable = CalculateTotalRepayable(request.ApprovedAmount, loan.InterestRate);

        loan.Approve(
            guarantorId,
            request.ApprovedAmount,
            frequency,
            totalRepayable,
            instalments,
            termMonths);

        await _auditLog.RecordAsync(guarantorId, "Loan.Approved", "Loan", loan.Id,
            $"RequestedAmount={loan.RequestedAmount}, RequestedFrequency={loan.RequestedFrequency}, " +
            $"RequestedTermMonths={loan.RequestedTermMonths}, RequestedInstalments={loan.RequestedInstalments}; " +
            $"ApprovedAmount={request.ApprovedAmount}, ApprovedFrequency={frequency}, " +
            $"ApprovedTermMonths={termMonths}, ApprovedInstalments={instalments}, TotalRepayable={totalRepayable}, " +
            $"ConfirmedWithMember={request.ConfirmedWithMember}", ct);
        await _loanRepository.SaveChangesAsync(ct);

        return ToLoanDto(loan, fund);
    }

    /// <summary>
    /// Guarantor rejects a pending loan request.
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

        if (fund.GuarantorId != guarantorId)
        {
            throw new InvalidLoanException("You do not have permission to reject this loan.");
        }

        loan.Reject(guarantorId, request.Reason);

        await _auditLog.RecordAsync(guarantorId, "Loan.Rejected", "Loan", loan.Id,
            request.Reason is not null ? $"Reason={request.Reason}" : null, ct);
        await _loanRepository.SaveChangesAsync(ct);

        return ToLoanDto(loan, fund);
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

        await _auditLog.RecordAsync(memberId, "Loan.Cancelled", "Loan", loan.Id,
            "Cancelled pending loan request", ct);
        await _loanRepository.SaveChangesAsync(ct);

        return ToLoanDto(loan, fund);
    }

    /// <summary>
    /// Guarantor cancels a pending, approved, or disbursement-pending (not yet
    /// disbursed) loan in one of their funds. Disbursed and completed loans can
    /// never be cancelled.
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
            throw new InvalidLoanException("You do not have permission to cancel this loan.");
        }

        loan.CancelByGuarantor(guarantorId);

        await _auditLog.RecordAsync(guarantorId, "Loan.Cancelled", "Loan", loan.Id,
            "Guarantor cancelled loan before disbursement", ct);
        await _loanRepository.SaveChangesAsync(ct);

        return ToLoanDto(loan, fund);
    }

    /// <summary>
    /// Returns pending loan requests for the Guarantor's review queue.
    /// </summary>
    public async Task<IReadOnlyList<LoanDto>> GetPendingRequestsForGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default)
    {
        var items = await _loanRepository.GetPendingForGuarantorAsync(guarantorId, ct);
        return items.Select(i => ToLoanDto(i.Loan, i.FundName, i.FundType, i.MemberDisplayName, i.MemberEmail)).ToList();
    }

    /// <summary>
    /// Returns the pending loan requests for a fund. Ownership is enforced by the caller.
    /// </summary>
    public async Task<IReadOnlyList<LoanDto>> GetLoansByFundAsync(
        Guid fundId,
        CancellationToken ct = default)
    {
        var items = await _loanRepository.GetByFundAsync(fundId, ct);
        return items.Select(i => ToLoanDto(i.Loan, i.FundName, i.FundType, i.MemberDisplayName, i.MemberEmail)).ToList();
    }

    /// <summary>
    /// Returns a single loan only if the specified Guarantor owns the loan's fund.
    /// Returns null otherwise (ownership-scoped lookup).
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
        if (fund is null || fund.GuarantorId != guarantorId)
        {
            return null;
        }

        return ToLoanDto(loan, fund);
    }

    /// <summary>
    /// Returns all loans for a member across all their funds.
    /// </summary>
    public async Task<IReadOnlyList<LoanDto>> GetLoansByMemberAsync(
        Guid memberId,
        CancellationToken ct = default)
    {
        var items = await _loanRepository.GetByMemberAsync(memberId, ct);
        return items.Select(i => ToLoanDto(i.Loan, i.FundName, i.FundType, i.MemberDisplayName, i.MemberEmail)).ToList();
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

    private static LoanDto ToLoanDto(Loan loan, Fund fund) => new()
    {
        Id = loan.Id,
        FundId = loan.FundId,
        FundName = fund.Name,
        FundType = fund.Type,
        MemberId = loan.MemberId,
        MemberDisplayName = string.Empty,
        MemberEmail = string.Empty,
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
    };

    private static LoanDto ToLoanDto(Loan loan, string fundName, Domain.Funds.FundType fundType, string memberDisplayName, string memberEmail) => new()
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
    };
}
