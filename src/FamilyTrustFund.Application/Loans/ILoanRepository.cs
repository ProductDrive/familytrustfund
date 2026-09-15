using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;

namespace FamilyTrustFund.Application.Loans;

/// <summary>
/// Persistence port for loans. Implemented by the infrastructure layer.
/// </summary>
public interface ILoanRepository
{
    Task<Loan?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Returns pending loan requests for funds owned by the specified Guarantor.
    /// Used for the Guarantor review queue.
    /// </summary>
    Task<IReadOnlyList<LoanWithDetails>> GetPendingForGuarantorAsync(Guid guarantorId, CancellationToken ct = default);

    /// <summary>
    /// Returns all loans for a specific fund, including member details.
    /// Ownership is enforced by the caller.
    /// </summary>
    Task<IReadOnlyList<LoanWithDetails>> GetByFundAsync(Guid fundId, CancellationToken ct = default);

    /// <summary>
    /// Returns all loans for a specific member across all their funds.
    /// </summary>
    Task<IReadOnlyList<LoanWithDetails>> GetByMemberAsync(Guid memberId, CancellationToken ct = default);

    /// <summary>
    /// Counts the active loans for a member across all their funds. A member may
    /// not hold more than one active loan at a time regardless of which fund(s)
    /// they belong to. A loan is "active" and blocks new requests while it is
    /// Pending, Approved or Disbursed (i.e. anything not yet terminal/resolved).
    /// </summary>
    Task<int> CountActiveByMemberAsync(Guid memberId, CancellationToken ct = default);

    /// <summary>
    /// Sums the total active disbursed loan amounts for a fund.
    /// Used to calculate available lending capacity.
    /// </summary>
    Task<decimal> SumActiveDisbursedByFundAsync(Guid fundId, CancellationToken ct = default);

    /// <summary>
    /// Checks whether a member already has a pending loan request in the fund.
    /// </summary>
    Task<bool> HasPendingRequestAsync(Guid memberId, Guid fundId, CancellationToken ct = default);

    void Add(Loan loan);

    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// Loan projection that includes member and fund details for display.
/// </summary>
public sealed class LoanWithDetails
{
    public Loan Loan { get; init; } = null!;
    public Guid MemberId { get; init; }
    public string MemberDisplayName { get; init; } = string.Empty;
    public string MemberEmail { get; init; } = string.Empty;
    public Guid FundId { get; init; }
    public string FundName { get; init; } = string.Empty;
    public FundType FundType { get; init; }
    public Guid GuarantorId { get; init; }
}
