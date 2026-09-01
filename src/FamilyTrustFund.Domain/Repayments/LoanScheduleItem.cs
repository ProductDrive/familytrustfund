using FamilyTrustFund.Domain.Loans;

namespace FamilyTrustFund.Domain.Repayments;

/// <summary>
/// A single scheduled repayment instalment within a <see cref="LoanSchedule"/>.
/// </summary>
public class LoanScheduleItem
{
    public Guid Id { get; private set; }
    public Guid ScheduleId { get; private set; }
    public int Sequence { get; private set; }
    public DateTime DueDateUtc { get; private set; }
    public decimal PrincipalDue { get; private set; }
    public decimal InterestDue { get; private set; }

    /// <summary>Total expected for this instalment (principal + interest).</summary>
    public decimal ExpectedAmount { get; private set; }

    public decimal PaidAmount { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }
    public ScheduleItemStatus Status { get; private set; } = ScheduleItemStatus.Scheduled;

    protected LoanScheduleItem()
    {
    }

    internal LoanScheduleItem(
        Guid scheduleId,
        int sequence,
        DateTime dueDateUtc,
        decimal expectedAmount,
        decimal principalDue,
        decimal interestDue)
    {
        Id = Guid.NewGuid();
        ScheduleId = scheduleId;
        Sequence = sequence;
        DueDateUtc = dueDateUtc;
        ExpectedAmount = Math.Round(expectedAmount, 2, MidpointRounding.AwayFromZero);
        PrincipalDue = Math.Round(principalDue, 2, MidpointRounding.AwayFromZero);
        InterestDue = Math.Round(interestDue, 2, MidpointRounding.AwayFromZero);
        Status = ScheduleItemStatus.Scheduled;
    }

    public void MarkPaid(decimal amount, DateTime paidAtUtc)
    {
        PaidAmount += amount;
        PaidAtUtc = paidAtUtc;
        Status = amount >= ExpectedAmount ? ScheduleItemStatus.Paid : ScheduleItemStatus.Scheduled;
    }

    /// <summary>
    /// Updates this item's expected amounts when a schedule is recalculated
    /// (e.g. after a confirmed lump-sum). Why this item remains but its
    /// principal is recalculated. Preserve the original via schedule revisions.
    /// </summary>
    internal void Recalculate(decimal expectedAmount, decimal principalDue, decimal interestDue)
    {
        ExpectedAmount = Math.Round(expectedAmount, 2, MidpointRounding.AwayFromZero);
        PrincipalDue = Math.Round(principalDue, 2, MidpointRounding.AwayFromZero);
        InterestDue = Math.Round(interestDue, 2, MidpointRounding.AwayFromZero);
    }

    internal void MarkOverdue()
    {
        if (Status == ScheduleItemStatus.Scheduled && PaidAmount < ExpectedAmount)
        {
            Status = ScheduleItemStatus.Overdue;
        }
    }

    public bool IsPaid => Status == ScheduleItemStatus.Paid;
    public bool IsOverdue => Status == ScheduleItemStatus.Overdue;
}

/// <summary>
/// Lifecycle of a single schedule instalment.
/// </summary>
public enum ScheduleItemStatus
{
    /// <summary>Not yet due or not yet paid.</summary>
    Scheduled = 1,

    /// <summary>Fully paid.</summary>
    Paid = 2,

    /// <summary>Past its due date and not fully settled.</summary>
    Overdue = 3,
}

/// <summary>
/// A repayment instalment that is fully or partially due.
/// </summary>
public interface ILoanScheduleItemProjection
{
    int Sequence { get; }
    DateTime DueDateUtc { get; }
    decimal ExpectedAmount { get; }
    decimal PaidAmount { get; }
    ScheduleItemStatus Status { get; }
}
