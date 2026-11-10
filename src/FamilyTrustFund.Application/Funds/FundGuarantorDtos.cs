using FamilyTrustFund.Domain.Funds;

namespace FamilyTrustFund.Application.Funds;

/// <summary>One Guarantor's relationship to a group fund, as shown to clients.</summary>
public sealed class FundGuarantorDto
{
    public Guid Id { get; init; }
    public Guid FundId { get; init; }
    public Guid GuarantorId { get; init; }
    public string GuarantorDisplayName { get; init; } = string.Empty;
    public string GuarantorEmail { get; init; } = string.Empty;
    public FundGuarantorRole Role { get; init; }
    public FundGuarantorStatus Status { get; init; }
    public decimal CommittedCapital { get; init; }
    public DateTime InvitedAtUtc { get; init; }
    public DateTime? RespondedAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public bool IsExpired { get; init; }
    public bool IsOriginator { get; init; }
}

/// <summary>A group-fund invitation or membership from the invited Guarantor's view.</summary>
public sealed class GuarantorInvitationDto
{
    public Guid MembershipId { get; init; }
    public Guid FundId { get; init; }
    public string FundName { get; init; } = string.Empty;
    public FundType FundType { get; init; }
    public Guid OriginatorId { get; init; }
    public string OriginatorDisplayName { get; init; } = string.Empty;
    public string OriginatorEmail { get; init; } = string.Empty;
    public decimal CommittedCapital { get; init; }
    public FundGuarantorStatus Status { get; init; }
    public DateTime InvitedAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public bool IsExpired { get; init; }
}

/// <summary>Invites an existing, approved Guarantor account to operate the fund.</summary>
public sealed class InviteGuarantorRequest
{
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// The invited Guarantor's committed capital for the fund. A planning figure
    /// only; it is never money deposited into the platform (ADR-002).
    /// </summary>
    public decimal CommittedCapital { get; init; }
}

/// <summary>Changes a Guarantor's own committed capital for a group fund.</summary>
public sealed class UpdateGuarantorCommitmentRequest
{
    public decimal CommittedCapital { get; init; }
}
