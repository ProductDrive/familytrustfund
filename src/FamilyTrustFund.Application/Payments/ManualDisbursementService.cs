using FamilyTrustFund.Application.Audit;
using FamilyTrustFund.Application.Evidence;
using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Application.Loans;
using FamilyTrustFund.Application.Repayments;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Payments;

namespace FamilyTrustFund.Application.Payments;

/// <summary>
/// Application service for the offline/manual loan disbursement workflow
/// (ADR-056). The Fund Originator reports that they paid the member outside the
/// payment provider; the member confirms receipt, and only then does the loan
/// become DISBURSED and the capital ledger record. Reporting never changes the
/// ledger (AGENTS §2.9, §3, §7).
/// </summary>
public class ManualDisbursementService
{
    /// <summary>Evidence resource type used for offline-disbursement proof.</summary>
    public const string EvidenceResourceType = "Disbursement";

    private readonly IManualDisbursementRepository _repository;
    private readonly ILoanRepository _loanRepository;
    private readonly IFundRepository _fundRepository;
    private readonly IEvidenceRepository _evidenceRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IFundAccessService _fundAccess;
    private readonly IFundGuarantorRepository _guarantorRepository;
    private readonly ICapitalFundingRepository _capitalFundingRepository;
    private readonly RepaymentService _repaymentService;
    private readonly IAuditLog _auditLog;

    public ManualDisbursementService(
        IManualDisbursementRepository repository,
        ILoanRepository loanRepository,
        IFundRepository fundRepository,
        IEvidenceRepository evidenceRepository,
        IPaymentRepository paymentRepository,
        IFundAccessService fundAccess,
        IFundGuarantorRepository guarantorRepository,
        ICapitalFundingRepository capitalFundingRepository,
        RepaymentService repaymentService,
        IAuditLog auditLog)
    {
        _repository = repository;
        _loanRepository = loanRepository;
        _fundRepository = fundRepository;
        _evidenceRepository = evidenceRepository;
        _paymentRepository = paymentRepository;
        _fundAccess = fundAccess;
        _guarantorRepository = guarantorRepository;
        _capitalFundingRepository = capitalFundingRepository;
        _repaymentService = repaymentService;
        _auditLog = auditLog;
    }

    /// <summary>
    /// The Fund Originator creates a draft report of an offline payment. The
    /// report is not yet visible to the member and does not affect the ledger.
    /// The reported amount must equal the approved loan amount.
    /// </summary>
    public async Task<ManualDisbursementDto> CreateDraftAsync(
        Guid guarantorId,
        CreateManualDisbursementRequest request,
        CancellationToken ct = default)
    {
        var loan = await _loanRepository.GetByIdAsync(request.LoanId, ct)
            ?? throw new InvalidPaymentException("Loan not found.");

        var fund = await _fundRepository.GetByIdAsync(loan.FundId, ct)
            ?? throw new InvalidPaymentException("Fund not found.");

        await EnsureOriginatorAsync(fund, guarantorId, "report an offline disbursement for this fund", ct);

        if (loan.Status != LoanStatus.Approved && loan.Status != LoanStatus.DisbursementPending)
        {
            throw new InvalidPaymentException("Only an approved loan can be disbursed.");
        }

        // Never double-disburse: an in-progress Paystack disbursement blocks the
        // offline path (ADR-056).
        var paystack = await _paymentRepository.GetByLoanAsync(loan.Id, ct);
        if (paystack is not null && paystack.Status != DisbursementStatus.Failed)
        {
            throw new InvalidPaymentException(
                "A Paystack disbursement for this loan is already in progress. Cancel it before reporting an offline payment.");
        }

        if (await _repository.GetLiveForLoanAsync(loan.Id, ct) is not null)
        {
            throw new InvalidPaymentException("An offline disbursement for this loan is already in progress.");
        }

        await EnsureGroupReadyAsync(fund, loan, ct);

        var approvedAmount = loan.ApprovedAmount ?? loan.RequestedAmount;
        var report = ManualDisbursement.CreateDraft(
            loan.Id,
            fund.Id,
            loan.MemberId,
            guarantorId,
            request.Amount,
            approvedAmount,
            request.Method,
            request.DestinationBankName,
            request.DestinationAccountLast4,
            request.Reference,
            request.Note);

        _repository.Add(report);
        await _auditLog.RecordAsync(guarantorId, "Disbursement.OfflineReported", "ManualDisbursement", report.Id,
            $"LoanId={loan.Id}, Amount={report.Amount:N2}, Method={report.Method}", ct);
        await _repository.SaveChangesAsync(ct);

        return await ToDtoAsync(report, fund, ct);
    }

    /// <summary>
    /// Submits a draft report to the member for confirmation. At least one
    /// proof-of-transfer file must already exist; the loan moves to
    /// DISBURSEMENT_PENDING but is not yet disbursed.
    /// </summary>
    public async Task<ManualDisbursementDto> SubmitAsync(
        Guid guarantorId,
        Guid reportId,
        CancellationToken ct = default)
    {
        var report = await _repository.GetByIdAsync(reportId, ct)
            ?? throw new InvalidPaymentException("Offline disbursement not found.");

        var fund = await _fundRepository.GetByIdAsync(report.FundId, ct)
            ?? throw new InvalidPaymentException("Fund not found.");

        await EnsureOriginatorAsync(fund, guarantorId, "submit this offline disbursement", ct);

        var evidence = await _evidenceRepository.GetForResourceAsync(EvidenceResourceType, report.Id, ct);
        if (evidence.Count == 0)
        {
            throw new InvalidPaymentException("Upload at least one proof of payment before submitting.");
        }

        var loan = await _loanRepository.GetByIdAsync(report.LoanId, ct)
            ?? throw new InvalidPaymentException("Loan not found.");

        if (loan.Status != LoanStatus.Approved && loan.Status != LoanStatus.DisbursementPending)
        {
            throw new InvalidPaymentException("This loan can no longer be disbursed.");
        }

        report.Submit();

        if (loan.Status == LoanStatus.Approved)
        {
            loan.MarkDisbursementPending();
        }

        await _auditLog.RecordAsync(guarantorId, "Disbursement.OfflineSubmitted", "ManualDisbursement", report.Id,
            $"LoanId={loan.Id}, Amount={report.Amount:N2}", ct);
        await _repository.SaveChangesAsync(ct);
        await _loanRepository.SaveChangesAsync(ct);

        return await ToDtoAsync(report, fund, ct);
    }

    /// <summary>
    /// Cancels an offline report (draft, awaiting the member, or disputed) and
    /// returns the loan to APPROVED so another disbursement can be attempted.
    /// This is the only way out of a report the member never answers (ADR-056).
    /// </summary>
    public async Task<ManualDisbursementDto> CancelAsync(
        Guid guarantorId,
        Guid reportId,
        string? reason = null,
        CancellationToken ct = default)
    {
        var report = await _repository.GetByIdAsync(reportId, ct)
            ?? throw new InvalidPaymentException("Offline disbursement not found.");

        var fund = await _fundRepository.GetByIdAsync(report.FundId, ct)
            ?? throw new InvalidPaymentException("Fund not found.");

        await EnsureOriginatorAsync(fund, guarantorId, "cancel this offline disbursement", ct);

        report.Cancel(reason);

        var loan = await _loanRepository.GetByIdAsync(report.LoanId, ct);
        if (loan is not null && loan.Status == LoanStatus.DisbursementPending)
        {
            loan.RevertDisbursementPendingToApproved();
        }

        await _auditLog.RecordAsync(guarantorId, "Disbursement.OfflineCancelled", "ManualDisbursement", report.Id,
            reason is not null ? $"Reason={reason}" : "Offline disbursement cancelled", ct);
        await _repository.SaveChangesAsync(ct);
        if (loan is not null)
        {
            await _loanRepository.SaveChangesAsync(ct);
        }

        return await ToDtoAsync(report, fund, ct);
    }

    /// <summary>
    /// The member confirms receipt of the offline payment. This is the
    /// authoritative event: the loan becomes DISBURSED, the repayment schedule
    /// is created, and the capital ledger records the confirmed manual funding
    /// (gross = net = approved amount, fee = 0). Guarded and idempotent so a
    /// retry can never double-disburse (AGENTS §7).
    /// </summary>
    public async Task<ManualDisbursementDto> ConfirmByMemberAsync(
        Guid memberId,
        Guid reportId,
        string? note = null,
        CancellationToken ct = default)
    {
        var report = await _repository.GetByIdAsync(reportId, ct)
            ?? throw new InvalidPaymentException("Offline disbursement not found.");

        var loan = await _loanRepository.GetByIdAsync(report.LoanId, ct)
            ?? throw new InvalidPaymentException("Loan not found.");

        var fund = await _fundRepository.GetByIdAsync(report.FundId, ct)
            ?? throw new InvalidPaymentException("Fund not found.");

        // Guarded, idempotent transition: throws if the report is no longer
        // awaiting this member.
        report.Confirm(memberId, note);

        if (loan.Status == LoanStatus.DisbursementPending)
        {
            loan.MarkDisbursed();
        }

        // Offline capital has no provider collection: the Guarantor paid the
        // member directly, so gross = net = approved amount and the fee is zero.
        // It is only recorded now that the member has confirmed receipt.
        var capital = CapitalTransaction.CreateManualConfirmed(
            fund.Id, loan.Id, report.ReportedByGuarantorId, report.Amount, $"MANUAL-{report.Id:N}");
        _capitalFundingRepository.Add(capital);

        // The loan is disbursed, so ensure its repayment schedule exists. This
        // mirrors the provider-confirmation path exactly (ADR-041).
        await _repaymentService.EnsureScheduleAsync(memberId, loan.Id, ct);

        await _auditLog.RecordAsync(memberId, "Disbursement.OfflineConfirmed", "ManualDisbursement", report.Id,
            $"LoanId={loan.Id}, Amount={report.Amount:N2}, CapitalRef={capital.ProviderReference}", ct);
        await _repository.SaveChangesAsync(ct);
        await _loanRepository.SaveChangesAsync(ct);
        await _capitalFundingRepository.SaveChangesAsync(ct);

        return await ToDtoAsync(report, fund, ct);
    }

    /// <summary>
    /// The member disputes receipt. The loan stays DISBURSEMENT_PENDING and the
    /// report stays live (blocking every other disbursement path) until the
    /// Guarantor resolves it by cancelling and retrying (ADR-056).
    /// </summary>
    public async Task<ManualDisbursementDto> DisputeByMemberAsync(
        Guid memberId,
        Guid reportId,
        string reason,
        CancellationToken ct = default)
    {
        var report = await _repository.GetByIdAsync(reportId, ct)
            ?? throw new InvalidPaymentException("Offline disbursement not found.");

        var fund = await _fundRepository.GetByIdAsync(report.FundId, ct)
            ?? throw new InvalidPaymentException("Fund not found.");

        report.Dispute(memberId, reason);

        await _auditLog.RecordAsync(memberId, "Disbursement.OfflineDisputed", "ManualDisbursement", report.Id,
            $"LoanId={report.LoanId}, Reason={reason}", ct);
        await _repository.SaveChangesAsync(ct);

        return await ToDtoAsync(report, fund, ct);
    }

    /// <summary>
    /// The most recent offline report for a loan, if the Guarantor may operate
    /// the fund; null otherwise.
    /// </summary>
    public async Task<ManualDisbursementDto?> GetLatestForLoanAsync(
        Guid loanId,
        Guid guarantorId,
        CancellationToken ct = default)
    {
        var report = await _repository.GetLatestForLoanAsync(loanId, ct);
        if (report is null)
        {
            return null;
        }

        var fund = await _fundRepository.GetByIdAsync(report.FundId, ct);
        if (fund is null || !await _fundAccess.CanAccessFundAsync(fund, guarantorId, ct))
        {
            return null;
        }

        return await ToDtoAsync(report, fund, ct);
    }

    /// <summary>
    /// A report if the given member owns it; null otherwise. Used to authorize
    /// member access to a report and its evidence.
    /// </summary>
    public async Task<ManualDisbursementDto?> GetForOwnerAsync(
        Guid reportId,
        Guid memberId,
        CancellationToken ct = default)
    {
        var report = await _repository.GetByIdAsync(reportId, ct);
        if (report is null || report.MemberId != memberId)
        {
            return null;
        }

        var fund = await _fundRepository.GetByIdAsync(report.FundId, ct);
        return await ToDtoAsync(report, fund, ct);
    }

    /// <summary>
    /// A report if the given Guarantor may operate the fund it belongs to; null
    /// otherwise. Used to authorize Guarantor access to a report and its
    /// evidence.
    /// </summary>
    public async Task<ManualDisbursementDto?> GetForGuarantorAsync(
        Guid reportId,
        Guid guarantorId,
        CancellationToken ct = default)
    {
        var report = await _repository.GetByIdAsync(reportId, ct);
        if (report is null)
        {
            return null;
        }

        var fund = await _fundRepository.GetByIdAsync(report.FundId, ct);
        if (fund is null || !await _fundAccess.CanAccessFundAsync(fund, guarantorId, ct))
        {
            return null;
        }

        return await ToDtoAsync(report, fund, ct);
    }

    /// <summary>Offline reports for the member's own loans.</summary>
    public async Task<IReadOnlyList<ManualDisbursementDto>> GetMineAsync(
        Guid memberId,
        CancellationToken ct = default)
    {
        var items = await _repository.GetForMemberAsync(memberId, ct);
        var results = new List<ManualDisbursementDto>();
        foreach (var item in items)
        {
            // A draft has not been submitted and is not yet visible to the member
            // (ADR-056); only reports the Guarantor has sent are returned.
            if (item.Report.Status == ManualDisbursementStatus.Draft)
            {
                continue;
            }

            results.Add(await ToDtoAsync(
                item.Report, item.FundId, item.FundName, item.MemberDisplayName, item.MemberEmail, ct));
        }

        return results;
    }

    private async Task EnsureOriginatorAsync(
        Fund fund,
        Guid guarantorId,
        string action,
        CancellationToken ct)
    {
        if (await _fundAccess.IsOriginatorAsync(fund.Id, guarantorId, ct))
        {
            return;
        }

        throw new InvalidPaymentException(fund.IsGroupFund
            ? $"Only the Fund Originator can {action}."
            : "You do not have permission to disburse this loan.");
    }

    private async Task EnsureGroupReadyAsync(Fund fund, Loan loan, CancellationToken ct)
    {
        if (!fund.IsGroupFund)
        {
            return;
        }

        var activeCount = await _guarantorRepository.CountActiveAsync(fund.Id, ct);
        if (activeCount < FundGuarantorService.MinActiveGuarantors)
        {
            throw new InvalidPaymentException(
                "A group fund needs at least two active Guarantors before it can disburse.");
        }

        var approvals = await _loanRepository.GetApprovalsAsync(loan.Id, ct);
        if (approvals.Count == 0 || approvals.Any(a => !a.IsApproved))
        {
            throw new InvalidPaymentException(
                "Every active Guarantor must approve this loan before it can be disbursed.");
        }
    }

    private async Task<ManualDisbursementDto> ToDtoAsync(ManualDisbursement report, Fund? fund, CancellationToken ct) =>
        await ToDtoAsync(
            report,
            fund?.Id ?? report.FundId,
            fund?.Name ?? string.Empty,
            string.Empty,
            string.Empty,
            ct);

    private async Task<ManualDisbursementDto> ToDtoAsync(
        ManualDisbursement report,
        Guid fundId,
        string fundName,
        string memberDisplayName,
        string memberEmail,
        CancellationToken ct)
    {
        var evidence = await _evidenceRepository.GetForResourceAsync(EvidenceResourceType, report.Id, ct);

        return new ManualDisbursementDto
        {
            Id = report.Id,
            LoanId = report.LoanId,
            FundId = fundId,
            FundName = fundName,
            MemberId = report.MemberId,
            MemberDisplayName = memberDisplayName,
            MemberEmail = memberEmail,
            ReportedByGuarantorId = report.ReportedByGuarantorId,
            Amount = report.Amount,
            Currency = report.Currency,
            Method = report.Method,
            DestinationBankName = report.DestinationBankName,
            DestinationAccountLast4 = report.DestinationAccountLast4,
            Reference = report.Reference,
            Note = report.Note,
            Status = report.Status,
            MemberNote = report.MemberNote,
            CancelReason = report.CancelReason,
            ReportedAtUtc = report.ReportedAtUtc,
            SubmittedAtUtc = report.SubmittedAtUtc,
            ConfirmedAtUtc = report.ConfirmedAtUtc,
            DisputedAtUtc = report.DisputedAtUtc,
            CancelledAtUtc = report.CancelledAtUtc,
            HasEvidence = evidence.Count > 0,
        };
    }
}
