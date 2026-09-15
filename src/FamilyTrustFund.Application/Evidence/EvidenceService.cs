using FamilyTrustFund.Application.Audit;
using FamilyTrustFund.Application.Storage;
using FamilyTrustFund.Domain.Evidence;

namespace FamilyTrustFund.Application.Evidence;

/// <summary>
/// Application service for payment-evidence attachments.
/// </summary>
/// <remarks>
/// Manages validation, storage registration and retrieval of evidence files.
/// Uploaded evidence is metadata + stored content; it never itself posts to the
/// ledger — confirmation of the owning financial resource is a separate,
/// authorised step (AGENTS §2.9, §9).
/// </remarks>
public class EvidenceService
{
    /// <summary>Container name used for contribution evidence.</summary>
    public const string ContributionContainer = "contribution";

    /// <summary>Container name used for repayment evidence.</summary>
    public const string RepaymentContainer = "repayment";

    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    };

    private readonly IEvidenceRepository _evidenceRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IAuditLog _auditLog;

    public EvidenceService(
        IEvidenceRepository evidenceRepository,
        IFileStorage fileStorage,
        IAuditLog auditLog)
    {
        _evidenceRepository = evidenceRepository;
        _fileStorage = fileStorage;
        _auditLog = auditLog;
    }

    /// <summary>
    /// Stores new evidence and registers its metadata against a financial
    /// resource.
    /// </summary>
    public async Task<PaymentEvidenceDto> UploadAsync(
        Guid uploaderUserId,
        string resourceType,
        Guid resourceId,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken ct = default)
    {
        if (content is null || content.Length == 0)
        {
            throw new InvalidEvidenceException("Evidence file is required.");
        }

        if (content.LongLength > MaxFileSizeBytes)
        {
            throw new InvalidEvidenceException("Evidence file is too large. Maximum size is 5 MB.");
        }

        if (!AllowedContentTypes.Contains(contentType))
        {
            throw new InvalidEvidenceException("Evidence type is not allowed. Use a PDF or an image.");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidEvidenceException("A file name is required.");
        }

        var container = ContainerFor(resourceType);

        // Store the content first using the server-generated key.
        var stored = await _fileStorage.StoreAsync(container, content, contentType, ct);

        var evidence = PaymentEvidence.Register(
            uploaderUserId,
            resourceType,
            resourceId,
            container,
            stored.ObjectKey,
            fileName.Trim(),
            contentType,
            stored.SizeBytes);

        _evidenceRepository.Add(evidence);
        await _auditLog.RecordAsync(uploaderUserId, "Evidence.Uploaded", resourceType, resourceId,
            $"FileName={stored.ObjectKey}, Size={stored.SizeBytes}, Type={contentType}", ct);
        await _evidenceRepository.SaveChangesAsync(ct);

        return ToDto(evidence);
    }

    /// <summary>
    /// Returns the registered metadata for a financial resource.
    /// </summary>
    public async Task<IReadOnlyList<PaymentEvidenceDto>> GetForResourceAsync(
        string resourceType,
        Guid resourceId,
        CancellationToken ct = default)
    {
        var items = await _evidenceRepository.GetForResourceAsync(resourceType, resourceId, ct);
        return items.Select(ToDto).ToList();
    }

    /// <summary>
    /// Returns all evidence uploaded by a given user (the member who made the
    /// payments). Only exposes the caller's own attachments.
    /// </summary>
    public async Task<IReadOnlyList<PaymentEvidenceDto>> GetMineAsync(
        Guid uploaderUserId,
        CancellationToken ct = default)
    {
        var items = await _evidenceRepository.GetByUploaderAsync(uploaderUserId, ct);
        return items.Select(ToDto).ToList();
    }

    /// <summary>
    /// Reads an evidence record and its stored content. Returns null if the
    /// evidence does not exist.
    /// </summary>
    /// <remarks>
    /// Authorization (that the caller may view this evidence) is enforced by the
    /// caller — the evidence's owning resource must belong to the caller or to a
    /// fund the Guarantor owns (AGENTS §9). This method returns raw storage
    /// metadata so the endpoint can serve it with the correct content type.
    /// </remarks>
    public async Task<(PaymentEvidence Evidence, StoredObject Storage)?> ReadAsync(
        Guid evidenceId,
        CancellationToken ct = default)
    {
        var evidence = await _evidenceRepository.GetByIdAsync(evidenceId, ct);
        if (evidence is null)
        {
            return null;
        }

        var storage = await _fileStorage.ReadAsync(evidence.StorageContainer, evidence.StorageKey, ct);
        if (storage is null)
        {
            return null;
        }

        return (evidence, storage);
    }

    private static string ContainerFor(string resourceType) => resourceType.ToLowerInvariant() switch
    {
        "contribution" => ContributionContainer,
        "repayment" => RepaymentContainer,
        _ => resourceType.ToLowerInvariant(),
    };

    private static PaymentEvidenceDto ToDto(PaymentEvidence e) => new()
    {
        Id = e.Id,
        UploadedByUserId = e.UploadedByUserId,
        ResourceType = e.ResourceType,
        ResourceId = e.ResourceId,
        OriginalFileName = e.OriginalFileName,
        ContentType = e.StoredContentType,
        SizeBytes = e.SizeBytes,
        UploadedAtUtc = e.UploadedAtUtc,
    };
}
