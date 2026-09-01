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

    Task AddAsync(LoanSchedule schedule, CancellationToken ct = default);

    Task AddAsync(Repayment repayment, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
