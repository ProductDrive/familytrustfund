using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Repayments;

namespace FamilyTrustFund.Application.Repayments;

/// <summary>
/// Repository for repayment schedules, schedule items and repayment records.
/// Shares the application DbContext so loan balance updates and repayment
/// records commit atomically (AGENTS §7).
/// </summary>
public interface IRepaymentRepository
{
    Task<Loan?> GetLoanAsync(Guid loanId, CancellationToken ct = default);

    Task<LoanSchedule?> GetCurrentScheduleAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<LoanSchedule>> GetAllSchedulesAsync(Guid loanId, CancellationToken ct = default);

    Task<LoanScheduleItem?> GetItemAsync(Guid itemId, CancellationToken ct = default);

    Task<IReadOnlyList<Repayment>> GetRepaymentsAsync(Guid loanId, CancellationToken ct = default);

    Task<(IReadOnlyList<RepaymentWithFund> Items, int TotalCount)> GetMyRepaymentsAsync(
        Guid memberId, int page, int pageSize, CancellationToken ct = default);

    Task AddAsync(LoanSchedule schedule, CancellationToken ct = default);

    /// <summary>
    /// Adds and saves a new schedule revision idempotently. When a concurrent
    /// request has already persisted the same (LoanId, Version), the pending
    /// copy is discarded and the winning schedule is returned (the unique index
    /// is the authoritative guard, AGENTS §7.3). Returns null only if the
    /// schedule still could not be created.
    /// </summary>
    Task<LoanSchedule?> SaveNewScheduleAsync(LoanSchedule schedule, CancellationToken ct = default);

    Task AddAsync(Repayment repayment, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
