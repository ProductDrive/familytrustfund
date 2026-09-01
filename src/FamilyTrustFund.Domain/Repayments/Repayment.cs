namespace FamilyTrustFund.Domain.Repayments;

/// <summary>
/// A recorded repayment transaction against a loan. Expected and actual
/// amounts are tracked separately so repayment surplus can be derived (AGENTS §2.7).
/// </summary>
public class Repayment
{
    public Guid Id { get; private set; }
    public Guid LoanId { get; private set; }
    public int ScheduleVersion { get; private set; }
    public RepaymentKind Kind { get; private set; }

    /// <summary>What was expected for this payment (an instalment or the settlement quote).</summary>
    public decimal ExpectedAmount { get; private set; }

    /// <summary>What the member actually paid.</summary>
    public decimal ActualAmount { get; private set; }

    /// <summary>Derived surplus = actual - expected.</summary>
    public decimal Surplus { get; private set; }

    public DateTime PaidAtUtc { get; private set; }

    public string? Note { get; private set; }

    protected Repayment()
    {
    }

    public Repayment(
        Guid loanId,
        int scheduleVersion,
        RepaymentKind kind,
        decimal expectedAmount,
        decimal actualAmount,
        DateTime paidAtUtc,
        string? note = null)
    {
        if (loanId == Guid.Empty)
        {
            throw new InvalidRepaymentException("A loan is required for a repayment.");
        }

        if (actualAmount <= 0)
        {
            throw new InvalidRepaymentException("Repayment amount must be greater than zero.");
        }

        Id = Guid.NewGuid();
        LoanId = loanId;
        ScheduleVersion = scheduleVersion;
        Kind = kind;
        ExpectedAmount = Math.Round(expectedAmount, 2, MidpointRounding.AwayFromZero);
        ActualAmount = Math.Round(actualAmount, 2, MidpointRounding.AwayFromZero);
        Surplus = RepaymentRules.Surplus(ActualAmount, ExpectedAmount);
        PaidAtUtc = paidAtUtc;
        Note = note;
    }
}

/// <summary>
/// The kind of a repayment.
/// </summary>
public enum RepaymentKind
{
    /// <summary>A normal scheduled instalment.</summary>
    Scheduled = 1,

    /// <summary>A lump-sum payment that reduces the principal early.</summary>
    LumpSum = 2,

    /// <summary>Full settlement of the loan.</summary>
    FullSettlement = 3,
}
