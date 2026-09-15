using FamilyTrustFund.Application.Audit;
using FamilyTrustFund.Application.Evidence;
using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Application.Loans;
using FamilyTrustFund.Domain.Evidence;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Repayments;

namespace FamilyTrustFund.Application.Repayments;

/// <summary>
/// Application service for the manual (external/offline) repayment confirmation
/// workflow. A member declares a repayment with optional evidence; it stays
/// <see cref="PendingRepaymentStatus.PendingConfirmation"/> and does NOT touch
/// the ledger until the Guarantor confirms it. Only confirmation posts the
/// financial transaction — reusing the authoritative repayment path (AGENTS §2.9,
/// ADR-016/037).
/// </summary>
public class PendingRepaymentService
{
    private readonly IPendingRepaymentRepository _pendingRepository;
    private readonly ILoanRepository _loanRepository;
    private readonly IFundRepository _fundRepository;
    private readonly IEvidenceRepository _evidenceRepository;
    private readonly RepaymentService _repaymentService;
    private readonly IAuditLog _auditLog;

    public PendingRepaymentService(
        IPendingRepaymentRepository pendingRepository,
        ILoanRepository loanRepository,
        IFundRepository fundRepository,
        IEvidenceRepository evidenceRepository,
        RepaymentService repaymentService,
        IAuditLog auditLog)
    {
        _pendingRepository = pendingRepository;
        _loanRepository = loanRepository;
        _fundRepository = fundRepository;
        _evidenceRepository = evidenceRepository;
        _repaymentService = repaymentService;
        _auditLog = auditLog;
    }

    /// <summary>
    /// A member declares a manual repayment against one of their active loans.
    /// No financial state changes until confirmed.
    /// </summary>
    public async Task<PendingRepaymentDto> ReportAsync(
        Guid memberId,
        ReportPendingRepaymentRequest request,
        CancellationToken ct = default)
    {
        if (request.Amount <= 0)
        {
            throw new InvalidRepaymentException("Repayment amount must be greater than zero.");
        }

        var loan = await _loanRepository.GetByIdAsync(request.LoanId, ct)
            ?? throw new InvalidRepaymentException("Loan not found.");

        if (loan.MemberId != memberId)
        {
            throw new InvalidRepaymentException("A member can only declare repayments on their own loans.");
        }

        if (loan.Status != LoanStatus.Disbursed)
        {
            throw new InvalidRepaymentException("Only an active disbursed loan can be repaid.");
        }

        if (await _pendingRepository.HasPendingForLoanAsync(loan.Id, ct))
        {
            throw new InvalidRepaymentException(
                "A repayment for this loan is already awaiting confirmation. Complete it first.");
        }

        var pending = PendingRepayment.Report(
            loan.Id,
            memberId,
            request.Amount,
            request.Kind,
            request.Reference,
            request.Note);

        _pendingRepository.Add(pending);
        await _auditLog.RecordAsync(memberId, "Repayment.Declared", "PendingRepayment", pending.Id,
            $"LoanId={loan.Id}, Amount={request.Amount:N2}, Kind={request.Kind}", ct);
        await _pendingRepository.SaveChangesAsync(ct);

        var fund = await _fundRepository.GetByIdAsync(loan.FundId, ct);
        var dto = ToDto(pending, loan.FundId, fund?.Name ?? string.Empty, string.Empty, string.Empty);
        return await WithHasEvidenceAsync(dto, ct);
    }

    /// <summary>
    /// Returns the pending repayments awaiting the Guarantor's confirmation.
    /// </summary>
    public async Task<IReadOnlyList<PendingRepaymentDto>> GetPendingForGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default)
    {
        var items = await _pendingRepository.GetPendingForGuarantorAsync(guarantorId, ct);
        var results = new List<PendingRepaymentDto>();
        foreach (var item in items)
        {
            results.Add(await WithHasEvidenceAsync(
                ToDto(item.Pending, item.FundId, item.FundName, item.MemberDisplayName, item.MemberEmail), ct));
        }

        return results;
    }

    /// <summary>Returns a member's pending repayments (access restricted by the caller).</summary>
    public async Task<IReadOnlyList<PendingRepaymentDto>> GetMineAsync(
        Guid memberId,
        CancellationToken ct = default)
    {
        var items = await _pendingRepository.GetByMemberAsync(memberId, ct);
        var results = new List<PendingRepaymentDto>();
        foreach (var item in items)
        {
            results.Add(await WithHasEvidenceAsync(
                ToDto(item.Pending, item.FundId, item.FundName, item.MemberDisplayName, item.MemberEmail), ct));
        }

        return results;
    }

    /// <summary>
    /// Returns a pending repayment if the given member owns it; null otherwise.
    /// Used to authorize member access to a repayment and its evidence.
    /// </summary>
    public async Task<PendingRepaymentDto?> GetForOwnerAsync(
        Guid pendingId,
        Guid memberId,
        CancellationToken ct = default)
    {
        var pending = await _pendingRepository.GetByIdAsync(pendingId, ct);
        if (pending is null || pending.MemberId != memberId)
        {
            return null;
        }

        var (fundName, fundId) = await ResolveFundAsync(pending.LoanId, ct);
        return await WithHasEvidenceAsync(
            ToDto(pending, fundId, fundName, string.Empty, string.Empty), ct);
    }

    /// <summary>
    /// Returns a pending repayment if the given Guarantor owns the fund it
    /// belongs to; null otherwise. Used to authorize Guarantor access.
    /// </summary>
    public async Task<PendingRepaymentDto?> GetForGuarantorAsync(
        Guid pendingId,
        Guid guarantorId,
        CancellationToken ct = default)
    {
        var pending = await _pendingRepository.GetByIdAsync(pendingId, ct);
        if (pending is null)
        {
            return null;
        }

        var loan = await _loanRepository.GetByIdAsync(pending.LoanId, ct)
            ?? throw new InvalidRepaymentException("Loan not found.");

        var fund = await _fundRepository.GetByIdAsync(loan.FundId, ct);
        if (fund is null || fund.GuarantorId != guarantorId)
        {
            return null;
        }

        return await WithHasEvidenceAsync(
            ToDto(pending, fund.Id, fund.Name, string.Empty, string.Empty), ct);
    }

    /// <summary>
    /// Guarantor confirms a pending repayment, posting the financial transaction
    /// to the ledger through the authoritative repayment path. Idempotent: only a
    /// still-pending repayment can be confirmed (AGENTS §2.7, §7).
    /// </summary>
    public async Task<ConfirmPendingRepaymentResult> ConfirmAsync(
        Guid guarantorId,
        ConfirmPendingRepaymentRequest request,
        CancellationToken ct = default)
    {
        var pending = await _pendingRepository.GetByIdAsync(request.PendingRepaymentId, ct)
            ?? throw new InvalidRepaymentException("Pending repayment not found.");

        var loan = await _loanRepository.GetByIdAsync(pending.LoanId, ct)
            ?? throw new InvalidRepaymentException("Loan not found.");

        var fund = await _fundRepository.GetByIdAsync(loan.FundId, ct)
            ?? throw new InvalidRepaymentException("Fund not found.");

        if (fund.GuarantorId != guarantorId)
        {
            throw new InvalidRepaymentException("You do not have permission to confirm this repayment.");
        }

        // Guarded, idempotent transition: throws if no longer pending so a retry
        // can never double-post. Saved atomically with the ledger post below.
        pending.Confirm(request.Note);

        await PostFinancialAsync(pending, ct);
        await MarkEvidenceReviewedAsync(pending.Id, guarantorId, ct);
        await _auditLog.RecordAsync(guarantorId, "Repayment.Confirmed", "PendingRepayment", pending.Id,
            request.Note is not null
                ? $"LoanId={loan.Id}, Amount={pending.Amount:N2}, Kind={pending.Kind}, Note={request.Note}"
                : $"LoanId={loan.Id}, Amount={pending.Amount:N2}, Kind={pending.Kind}", ct);

        // The posting path saves the shared DbContext, committing the pending
        // confirmation, the posted Repayment ledger record, the loan balance and
        // the audit/evidence review atomically.
        var fundName = fund.Name;
        var dto = await WithHasEvidenceAsync(ToDto(pending, fund.Id, fundName, string.Empty, string.Empty), ct);
        return new ConfirmPendingRepaymentResult { Pending = dto, RepaymentPosted = true };
    }

    /// <summary>
    /// Guarantor rejects a pending repayment. A rejected repayment never posts
    /// to the ledger. Idempotent: only a still-pending repayment can be rejected.
    /// </summary>
    public async Task<PendingRepaymentDto> RejectAsync(
        Guid guarantorId,
        RejectPendingRepaymentRequest request,
        CancellationToken ct = default)
    {
        var pending = await _pendingRepository.GetByIdAsync(request.PendingRepaymentId, ct)
            ?? throw new InvalidRepaymentException("Pending repayment not found.");

        var loan = await _loanRepository.GetByIdAsync(pending.LoanId, ct)
            ?? throw new InvalidRepaymentException("Loan not found.");

        var fund = await _fundRepository.GetByIdAsync(loan.FundId, ct)
            ?? throw new InvalidRepaymentException("Fund not found.");

        if (fund.GuarantorId != guarantorId)
        {
            throw new InvalidRepaymentException("You do not have permission to reject this repayment.");
        }

        pending.Reject(request.Reason);

        await MarkEvidenceReviewedAsync(pending.Id, guarantorId, ct);
        await _auditLog.RecordAsync(guarantorId, "Repayment.Rejected", "PendingRepayment", pending.Id,
            request.Reason is not null ? $"Reason={request.Reason}" : null, ct);
        await _pendingRepository.SaveChangesAsync(ct);

        return await WithHasEvidenceAsync(
            ToDto(pending, fund.Id, fund.Name, string.Empty, string.Empty), ct);
    }

    /// <summary>
    /// Posts the confirmed repayment through the authoritative repayment engine so
    /// the loan balance, schedule and repayment ledger are updated together.
    /// </summary>
    private async Task PostFinancialAsync(PendingRepayment pending, CancellationToken ct)
    {
        var paymentRequest = new MakeRepaymentRequest
        {
            LoanId = pending.LoanId,
            Amount = pending.Amount,
            Note = pending.Note,
        };

        switch (pending.Kind)
        {
            case RepaymentKind.FullSettlement:
                await _repaymentService.SettleAsync(pending.MemberId, paymentRequest, ct);
                break;
            case RepaymentKind.LumpSum:
                await _repaymentService.MakePaymentAsync(pending.MemberId, paymentRequest, RepaymentKind.LumpSum, ct);
                break;
            default:
                await _repaymentService.MakePaymentAsync(pending.MemberId, paymentRequest, RepaymentKind.Scheduled, ct);
                break;
        }
    }

    private async Task MarkEvidenceReviewedAsync(Guid pendingId, Guid reviewerId, CancellationToken ct)
    {
        var evidence = await _evidenceRepository.GetForResourceAsync("Repayment", pendingId, ct);
        foreach (var item in evidence)
        {
            item.MarkReviewed(reviewerId);
        }
    }

    private async Task<(string FundName, Guid FundId)> ResolveFundAsync(Guid loanId, CancellationToken ct)
    {
        var loan = await _loanRepository.GetByIdAsync(loanId, ct);
        if (loan is null)
        {
            return (string.Empty, Guid.Empty);
        }

        var fund = await _fundRepository.GetByIdAsync(loan.FundId, ct);
        return (fund?.Name ?? string.Empty, fund?.Id ?? loan.FundId);
    }

    private async Task<PendingRepaymentDto> WithHasEvidenceAsync(PendingRepaymentDto dto, CancellationToken ct)
    {
        var evidence = await _evidenceRepository.GetForResourceAsync("Repayment", dto.Id, ct);
        dto.HasEvidence = evidence.Count > 0;
        return dto;
    }

    private static PendingRepaymentDto ToDto(
        PendingRepayment p,
        Guid fundId,
        string fundName,
        string memberDisplayName,
        string memberEmail) => new()
    {
        Id = p.Id,
        LoanId = p.LoanId,
        MemberId = p.MemberId,
        FundId = fundId,
        FundName = fundName,
        MemberDisplayName = memberDisplayName,
        MemberEmail = memberEmail,
        Amount = p.Amount,
        Kind = p.Kind,
        Reference = p.Reference,
        Note = p.Note,
        Status = p.Status,
        RejectionReason = p.RejectionReason,
        ConfirmationNote = p.ConfirmationNote,
        ReportedAtUtc = p.ReportedAtUtc,
        ConfirmedAtUtc = p.ConfirmedAtUtc,
        RejectedAtUtc = p.RejectedAtUtc,
    };
}
