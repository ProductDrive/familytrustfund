using System;

namespace FamilyTrustFund.Application.Funds;

/// <summary>
/// Server-calculated transition state for a Family fund (ADR-008). Family
/// Contributions and Guarantor Committed Capital are always shown as separate
/// pools; the transition never happens automatically.
/// </summary>
public sealed class FundTransitionStatusDto
{
    public Guid FundId { get; init; }

    public bool IsFamilyFund { get; init; }

    public bool IsTransitioned { get; init; }

    public decimal CommittedCapital { get; init; }

    public decimal TotalFamilyContributions { get; init; }

    /// <summary>
    /// True when this is an Active Family fund that has not yet transitioned and
    /// total Family Contributions have reached or exceeded Committed Capital.
    /// </summary>
    public bool IsEligible { get; init; }

    /// <summary>Contributions still needed to reach the threshold (0 once reached).</summary>
    public decimal RemainingToThreshold { get; init; }
}

/// <summary>
/// Confirmation of a completed, one-way transition to Family Capital.
/// </summary>
public sealed class FundTransitionResultDto
{
    public Guid FundId { get; init; }

    public decimal CommittedCapital { get; init; }

    public decimal TotalFamilyContributions { get; init; }

    public DateTime TransitionedAtUtc { get; init; }
}