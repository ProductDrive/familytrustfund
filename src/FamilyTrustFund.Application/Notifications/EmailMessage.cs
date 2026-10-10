namespace FamilyTrustFund.Application.Notifications;

/// <summary>
/// A single outbound transactional email. Kept provider-agnostic so the domain
/// and application layers never depend on the concrete sender (AGENTS §3).
/// </summary>
public sealed record EmailMessage
{
    public required string To { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }

    /// <summary>Optional display name shown to the recipient.</summary>
    public string? DisplayName { get; init; }
}