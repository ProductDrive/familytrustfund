using FamilyTrustFund.Domain.Loans;

namespace FamilyTrustFund.Domain.Repayments;

/// <summary>
/// A versioned repayment schedule for a loan. A new revision is created each
/// time the remaining schedule is recalculated (e.g. after a confirmed
/// lump-sum). Historical revisions are preserved for audit (AGENTS §2.6).
/// </summary>
public class LoanSchedule
{
    private readonly List<LoanScheduleItem> _items = new();

    public Guid Id { get; private set; }
    public Guid LoanId { get; private set; }

    /// <summary>1-based revision number. The latest revision is the current schedule.</summary>
    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public IReadOnlyList<LoanScheduleItem> Items => _items;

    protected LoanSchedule()
    {
    }

    /// <summary>
    /// Creates the first (v1) schedule for a loan when it is about to be
    /// disbursed. Terms are locked from the approved loan.
    /// </summary>
    public static LoanSchedule CreateV1(
        Guid loanId,
        decimal approvedAmount,
        decimal totalRepayable,
        RepaymentFrequency frequency,
        int termInInstallments,
        DateTime? startDateUtc = null)
    {
        if (loanId == Guid.Empty)
        {
            throw new InvalidRepaymentException("A loan is required for a repayment schedule.");
        }

        var schedule = new LoanSchedule
        {
            Id = Guid.NewGuid(),
            LoanId = loanId,
            Version = 1,
            CreatedAtUtc = DateTime.UtcNow,
        };

        var instalments = RepaymentRules.BuildV1Instalments(
            schedule.Id,
            approvedAmount,
            totalRepayable,
            frequency,
            termInInstallments,
            startDateUtc ?? DateTime.UtcNow);

        schedule._items.AddRange(instalments.Select(i =>
            new LoanScheduleItem(
                schedule.Id,
                i.Sequence,
                i.DueDateUtc,
                i.ExpectedAmount,
                i.PrincipalDue,
                i.InterestDue)));

        return schedule;
    }

    /// <summary>
    /// Creates the next revision after a confirmed lump-sum: items with paid
    /// amounts are carried forward unchanged, fully-unpaid instalments are
    /// amortised across the remaining outstanding principal AND the remaining
    /// interest while preserving the repayment frequency. Interest is never
    /// discarded by a lump-sum (flat-rate external loans).
    /// </summary>
    public static LoanSchedule CreateRevision(
        LoanSchedule current,
        decimal remainingOutstanding,
        RepaymentFrequency frequency,
        DateTime? startDateUtc = null)
    {
        var schedule = new LoanSchedule
        {
            Id = Guid.NewGuid(),
            LoanId = current.LoanId,
            Version = current.Version + 1,
            CreatedAtUtc = DateTime.UtcNow,
        };

        var paidItems = current.Items.Where(i => i.PaidAmount > 0m).ToList();
        schedule._items.AddRange(paidItems);

        var unpaidItems = current.Items.Where(i => i.PaidAmount == 0m).ToList();
        if (unpaidItems.Count == 0 || (remainingOutstanding <= 0 && unpaidItems.Sum(i => i.InterestDue) <= 0))
        {
            return schedule;
        }

        var instalments = RepaymentRules.BuildAmortisedInstalments(
            schedule.Id,
            remainingOutstanding,
            unpaidItems.Sum(i => i.InterestDue),
            unpaidItems.Count,
            frequency,
            startDateUtc ?? DateTime.UtcNow);

        schedule._items.AddRange(instalments.Select(i =>
            new LoanScheduleItem(
                schedule.Id,
                i.Sequence,
                i.DueDateUtc,
                i.ExpectedAmount,
                i.PrincipalDue,
                i.InterestDue)));

        return schedule;
    }

    /// <summary>
    /// The total amount still owed on this schedule: the sum of every
    /// instalment's unpaid expected amount (principal + interest,
    /// AGENTS §2.6). A loan is fully repaid only when this reaches zero.
    /// </summary>
    public decimal RemainingObligation()
    {
        return Items.Where(i => !i.IsPaid).Sum(i => i.ExpectedAmount - i.PaidAmount);
    }

    /// <summary>Remaining unpaid interest on this schedule.</summary>
    public decimal RemainingInterest()
    {
        return Items.Where(i => !i.IsPaid).Sum(i => i.InterestDue);
    }

    /// <summary>
    /// Marks every unpaid instalment as zeroed when the loan is settled/closed.
    /// Historical revisions retain the original values.
    /// </summary>
    public void Close()
    {
        foreach (var item in _items.Where(i => !i.IsPaid))
        {
            item.Recalculate(0m, 0m, 0m);
        }
    }
}

/// <summary>
/// Validation exception for repayment operations.
/// </summary>
public class InvalidRepaymentException : Exception
{
    public InvalidRepaymentException(string message) : base(message)
    {
    }
}
