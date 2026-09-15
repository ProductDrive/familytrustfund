using FamilyTrustFund.Domain.Repayments;

namespace FamilyTrustFund.Application.Repayments;

/// <summary>
/// Persistence port for manually declared (pending) repayments and the
/// Guarantor confirmation workflow. Shares the application DbContext so the
/// pending-repayment status transition, the posted <see cref="Repayment"/>
/// ledger record and the loan balance update commit atomically.
/// </summary>
public interface IPendingRepaymentRepository
{
    Task<PendingRepayment?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<bool> HasPendingForLoanAsync(Guid loanId, CancellationToken ct = default);

    Task<bool> HasPendingByMemberAndFundAsync(
        Guid memberId,
        Guid fundId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns pending repayments across funds owned by the specified Guarantor,
    /// with member and fund details for the confirmation queue.
    /// </summary>
    Task<IReadOnlyList<PendingRepaymentWithDetails>> GetPendingForGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default);

    /// <summary>Returns a member's pending repayments across their loans.</summary>
    Task<IReadOnlyList<PendingRepaymentWithDetails>> GetByMemberAsync(
        Guid memberId,
        CancellationToken ct = default);

    void Add(PendingRepayment pending);

    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// Pending-repayment projection that includes member and fund details for display.
/// </summary>
public sealed class PendingRepaymentWithDetails
{
    public PendingRepayment Pending { get; init; } = null!;
    public Guid MemberId { get; init; }
    public string MemberDisplayName { get; init; } = string.Empty;
    public string MemberEmail { get; init; } = string.Empty;
    public Guid FundId { get; init; }
    public string FundName { get; init; } = string.Empty;
    public Guid GuarantorId { get; init; }
}
