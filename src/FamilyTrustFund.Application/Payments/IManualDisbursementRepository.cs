using FamilyTrustFund.Domain.Payments;

namespace FamilyTrustFund.Application.Payments;

/// <summary>
/// Persistence port for offline/manual loan disbursements reported by a
/// Guarantor and confirmed by the member (ADR-056). Shares the application
/// DbContext so the report transition, the loan status change and the capital
/// ledger entry commit atomically.
/// </summary>
public interface IManualDisbursementRepository
{
    Task<ManualDisbursement?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>The most recent offline report for a loan, whatever its status.</summary>
    Task<ManualDisbursement?> GetLatestForLoanAsync(Guid loanId, CancellationToken ct = default);

    /// <summary>
    /// The live report (Draft, PendingMemberConfirmation or Disputed) for a
    /// loan, if any. A live report blocks every other disbursement path so a
    /// loan can never be double-disbursed (ADR-056).
    /// </summary>
    Task<ManualDisbursement?> GetLiveForLoanAsync(Guid loanId, CancellationToken ct = default);

    /// <summary>
    /// Offline reports across funds operated by the specified Guarantor, with
    /// member and fund details for the review queue.
    /// </summary>
    Task<IReadOnlyList<ManualDisbursementWithDetails>> GetForGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default);

    /// <summary>Offline reports for a member's own loans.</summary>
    Task<IReadOnlyList<ManualDisbursementWithDetails>> GetForMemberAsync(
        Guid memberId,
        CancellationToken ct = default);

    void Add(ManualDisbursement report);

    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// Offline-disbursement projection that includes member and fund details for
/// display.
/// </summary>
public sealed class ManualDisbursementWithDetails
{
    public ManualDisbursement Report { get; init; } = null!;
    public Guid MemberId { get; init; }
    public string MemberDisplayName { get; init; } = string.Empty;
    public string MemberEmail { get; init; } = string.Empty;
    public Guid FundId { get; init; }
    public string FundName { get; init; } = string.Empty;
    public Guid GuarantorId { get; init; }
}
