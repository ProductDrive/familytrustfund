namespace FamilyTrustFund.Domain.Evidence;

/// <summary>
/// Metadata for a payment-evidence attachment (e.g. a receipt screenshot or
/// bank-details proof) uploaded against a financial resource such as a manual
/// contribution or repayment.
/// </summary>
/// <remarks>
/// The physical content lives in object storage, addressed by a server-generated
/// <see cref="StorageContainer"/>/<see cref="StorageKey"/>. This entity holds only
/// metadata and the original display name. Uploaded evidence is never itself
/// proof that changes the ledger — only an authorised confirm/reject transition
/// changes financial state (AGENTS §2.9, §9, ADR-016).
///
/// The storage container identifies the owning resource type (e.g. "contribution"
/// or "repayment") and <see cref="ResourceId"/> identifies the specific resource.
/// Client-supplied filenames are never used as filesystem paths.
/// </remarks>
public class PaymentEvidence
{
    public Guid Id { get; private set; }

    /// <summary>User who uploaded the evidence (the member who made the payment).</summary>
    public Guid UploadedByUserId { get; private set; }

    /// <summary>Resource type the evidence belongs to, e.g. "contribution".</summary>
    public string ResourceType { get; private set; } = string.Empty;

    /// <summary>Id of the financial resource (e.g. a FundContribution).</summary>
    public Guid ResourceId { get; private set; }

    /// <summary>Storage container (form of the resource type).</summary>
    public string StorageContainer { get; private set; } = string.Empty;

    /// <summary>Server-generated storage key; never derived from user input.</summary>
    public string StorageKey { get; private set; } = string.Empty;

    /// <summary>Original display filename supplied by the user (metadata only).</summary>
    public string OriginalFileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    /// <summary>Detected or validated media type of the content.</summary>
    public string StoredContentType => ContentType;

    public long SizeBytes { get; private set; }
    public DateTime UploadedAtUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>Id of the Guarantor who authored a confirm/reject decision, if any.</summary>
    public Guid? ReviewedByUserId { get; private set; }

    public DateTime? ReviewedAtUtc { get; private set; }

    protected PaymentEvidence()
    {
    }

    /// <summary>
    /// Registers a newly stored evidence attachment against a financial resource.
    /// </summary>
    public static PaymentEvidence Register(
        Guid uploadedByUserId,
        string resourceType,
        Guid resourceId,
        string storageContainer,
        string storageKey,
        string originalFileName,
        string contentType,
        long sizeBytes)
    {
        if (uploadedByUserId == Guid.Empty)
        {
            throw new InvalidEvidenceException("An uploader is required.");
        }

        if (string.IsNullOrWhiteSpace(resourceType))
        {
            throw new InvalidEvidenceException("A resource type is required.");
        }

        if (resourceId == Guid.Empty)
        {
            throw new InvalidEvidenceException("A resource is required.");
        }

        if (string.IsNullOrWhiteSpace(storageContainer) || string.IsNullOrWhiteSpace(storageKey))
        {
            throw new InvalidEvidenceException("Storage location is required.");
        }

        if (string.IsNullOrWhiteSpace(originalFileName) || originalFileName.Length > 255)
        {
            throw new InvalidEvidenceException("A valid original file name is required.");
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new InvalidEvidenceException("A content type is required.");
        }

        if (sizeBytes <= 0)
        {
            throw new InvalidEvidenceException("Evidence must not be empty.");
        }

        return new PaymentEvidence
        {
            Id = Guid.NewGuid(),
            UploadedByUserId = uploadedByUserId,
            ResourceType = resourceType.Trim(),
            ResourceId = resourceId,
            StorageContainer = storageContainer.Trim(),
            StorageKey = storageKey.Trim(),
            OriginalFileName = originalFileName.Trim(),
            ContentType = contentType.Trim().ToLowerInvariant(),
            SizeBytes = sizeBytes,
            UploadedAtUtc = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Records that a Guarantor reviewed this evidence as part of confirming or
    /// rejecting the associated financial resource.
    /// </summary>
    public void MarkReviewed(Guid reviewerUserId)
    {
        if (reviewerUserId == Guid.Empty)
        {
            throw new InvalidEvidenceException("A reviewer is required.");
        }

        ReviewedByUserId = reviewerUserId;
        ReviewedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Whether the evidence is attached to the given resource type and id.
    /// </summary>
    public bool BelongsTo(string resourceType, Guid resourceId) =>
        string.Equals(resourceType, ResourceType, StringComparison.OrdinalIgnoreCase)
        && ResourceId == resourceId;
}
