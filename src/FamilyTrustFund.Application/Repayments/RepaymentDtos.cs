using FamilyTrustFund.Domain.Repayments;

namespace FamilyTrustFund.Application.Repayments;

/// <summary>A single instalment returned to clients.</summary>
public sealed class ScheduleItemDto
{
    public Guid Id { get; init; }
    public int Sequence { get; init; }
    public DateTime DueDateUtc { get; init; }
    public decimal PrincipalDue { get; init; }
    public decimal InterestDue { get; init; }
    public decimal ExpectedAmount { get; init; }
    public decimal PaidAmount { get; init; }
    public DateTime? PaidAtUtc { get; init; }
    public ScheduleItemStatus Status { get; init; }
}

/// <summary>A repayment schedule revision returned to clients.</summary>
public sealed class ScheduleDto
{
    public Guid Id { get; init; }
    public Guid LoanId { get; init; }
    public int Version { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public bool IsCurrent { get; init; }
    public IReadOnlyList<ScheduleItemDto> Items { get; init; } = Array.Empty<ScheduleItemDto>();
}

/// <summary>Request to record a payment against a loan.</summary>
public sealed class MakeRepaymentRequest
{
    public Guid LoanId { get; init; }
    public decimal Amount { get; init; }
    public string? Note { get; init; }
}

/// <summary>A recorded repayment returned to clients.</summary>
public sealed class RepaymentDto
{
    public Guid Id { get; init; }
    public Guid LoanId { get; init; }
    public int ScheduleVersion { get; init; }
    public RepaymentKind Kind { get; init; }
    public decimal ExpectedAmount { get; init; }
    public decimal ActualAmount { get; init; }
    public decimal Surplus { get; init; }
    public DateTime PaidAtUtc { get; init; }
    public string? Note { get; init; }
}

/// <summary>Server-calculated summary of a loan's repayment position.</summary>
public sealed class RepaymentSummaryDto
{
    public decimal OutstandingBalance { get; init; }
    public decimal TotalExpected { get; init; }
    public decimal TotalPaid { get; init; }
    public decimal TotalSurplus { get; init; }
    public int OverdueItems { get; init; }
    public decimal OverdueAmount { get; init; }

    /// <summary>Server-calculated amount required to settle the loan now.</summary>
    public decimal SettlementQuote { get; init; }
}
