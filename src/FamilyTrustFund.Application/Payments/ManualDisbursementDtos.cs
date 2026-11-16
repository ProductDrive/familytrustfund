using FamilyTrustFund.Domain.Payments;

namespace FamilyTrustFund.Application.Payments;

/// <summary>
/// Guarantor request to report an offline/manual loan disbursement. The amount
/// must equal the approved loan amount; the member receives exactly that amount
/// and there is no provider fee on this path (ADR-056).
/// </summary>
public sealed class CreateManualDisbursementRequest
{
    public Guid LoanId { get; init; }
    public decimal Amount { get; init; }
    public ManualDisbursementMethod Method { get; init; }

    /// <summary>Destination bank name, when paid by transfer. Optional.</summary>
    public string? DestinationBankName { get; init; }

    /// <summary>Last four digits of the destination account, when known. Optional.</summary>
    public string? DestinationAccountLast4 { get; init; }

    /// <summary>Guarantor-supplied transfer reference. Optional.</summary>
    public string? Reference { get; init; }

    public string? Note { get; init; }
}

/// <summary>Guarantor instruction to submit a draft report to the member.</summary>
public sealed class SubmitManualDisbursementRequest
{
    public Guid ReportId { get; init; }
}

/// <summary>Guarantor instruction to cancel an offline report.</summary>
public sealed class CancelManualDisbursementRequest
{
    public Guid ReportId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Member confirmation that an offline disbursement was received.</summary>
public sealed class ConfirmManualDisbursementRequest
{
    public Guid ReportId { get; init; }
    public string? Note { get; init; }
}

/// <summary>Member dispute that an offline disbursement was not received.</summary>
public sealed class DisputeManualDisbursementRequest
{
    public Guid ReportId { get; init; }
    public string Reason { get; init; } = string.Empty;
}

/// <summary>An offline/manual disbursement returned to clients.</summary>
public sealed class ManualDisbursementDto
{
    public Guid Id { get; init; }
    public Guid LoanId { get; init; }
    public Guid FundId { get; init; }
    public string FundName { get; init; } = string.Empty;
    public Guid MemberId { get; init; }
    public string MemberDisplayName { get; init; } = string.Empty;
    public string MemberEmail { get; init; } = string.Empty;
    public Guid ReportedByGuarantorId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "NGN";
    public ManualDisbursementMethod Method { get; init; }
    public string? DestinationBankName { get; init; }
    public string? DestinationAccountLast4 { get; init; }
    public string? Reference { get; init; }
    public string? Note { get; init; }
    public ManualDisbursementStatus Status { get; init; }
    public string? MemberNote { get; init; }
    public string? CancelReason { get; init; }
    public DateTime ReportedAtUtc { get; init; }
    public DateTime? SubmittedAtUtc { get; init; }
    public DateTime? ConfirmedAtUtc { get; init; }
    public DateTime? DisputedAtUtc { get; init; }
    public DateTime? CancelledAtUtc { get; init; }

    /// <summary>Whether at least one proof-of-transfer file has been uploaded.</summary>
    public bool HasEvidence { get; set; }
}
