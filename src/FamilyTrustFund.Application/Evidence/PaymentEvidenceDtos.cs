namespace FamilyTrustFund.Application.Evidence;

/// <summary>
/// Server-registered metadata for an evidence attachment. The physical content
/// is served/downloaded by id separately and never addressed by a raw path.
/// </summary>
public sealed class PaymentEvidenceDto
{
    public Guid Id { get; init; }
    public Guid UploadedByUserId { get; init; }
    public string ResourceType { get; init; } = string.Empty;
    public Guid ResourceId { get; init; }
    public string OriginalFileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTime UploadedAtUtc { get; init; }
}
