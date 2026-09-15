using System.Security.Cryptography;
using FamilyTrustFund.Application.Storage;
using Microsoft.Extensions.Options;

namespace FamilyTrustFund.Infrastructure.Storage;

/// <summary>
/// Filesystem-backed implementation of <see cref="IFileStorage"/>.
/// </summary>
/// <remarks>
/// Files are stored under <c>&lt;RootPath&gt;/&lt;container&gt;/&lt;objectKey&gt;</c>.
/// Object keys are server-generated and never derived from user input, so path
/// traversal is not possible through a key. Sensitive content (payment evidence)
/// is stored outside the web root and only served through authorized endpoints.
/// This implementation can be replaced by an object-storage provider without
/// touching domain logic.
/// </remarks>
public class FileSystemStorage : IFileStorage
{
    private readonly string _rootPath;

    public FileSystemStorage(IOptions<FileSystemStorageOptions> options)
    {
        _rootPath = string.IsNullOrWhiteSpace(options.Value.RootPath)
            ? Path.Combine(Path.GetTempPath(), "familytrustfund-storage")
            : options.Value.RootPath;
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<StoredObject> StoreAsync(
        string container,
        byte[] content,
        string contentType,
        CancellationToken ct = default)
    {
        var objectKey = GenerateObjectKey(contentType);
        var fullPath = ResolvePath(container, objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, content, ct);

        return new StoredObject
        {
            Container = container,
            ObjectKey = objectKey,
            ContentType = contentType,
            SizeBytes = content.LongLength,
        };
    }

    public async Task<StoredObject?> ReadAsync(
        string container,
        string objectKey,
        CancellationToken ct = default)
    {
        var fullPath = ResolvePath(container, objectKey);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(fullPath, ct);

        return new StoredObject
        {
            Container = container,
            ObjectKey = objectKey,
            ContentType = InferContentType(objectKey),
            SizeBytes = bytes.LongLength,
            Content = bytes,
        };
    }

    public Task<bool> DeleteAsync(
        string container,
        string objectKey,
        CancellationToken ct = default)
    {
        var fullPath = ResolvePath(container, objectKey);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult(false);
        }

        File.Delete(fullPath);
        return Task.FromResult(true);
    }

    private string ResolvePath(string container, string objectKey)
    {
        var safeContainer = SanitizeSegment(container);
        var safeKey = SanitizeSegment(objectKey);
        return Path.Combine(_rootPath, safeContainer, safeKey);
    }

    /// <summary>
    /// Rejects any path-directory characters so user/provider segments can never
    /// escape the storage root (defense in depth; see AGENTS §9).
    /// </summary>
    private static string SanitizeSegment(string segment)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\' }).ToArray();
        var cleaned = segment
            .Select(c => invalid.Contains(c) ? '_' : c)
            .ToArray();
        return new string(cleaned);
    }

    /// <summary>
    /// Generates a unique, server-controlled object key. A file extension is
    /// derived from the content type (never from a user filename).
    /// </summary>
    private static string GenerateObjectKey(string contentType)
    {
        var extension = ExtensionForContentType(contentType);
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        return $"{DateTime.UtcNow:yyyyMMdd}/{id}{extension}";
    }

    private static string InferContentType(string objectKey)
    {
        var extension = Path.GetExtension(objectKey)?.ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream",
        };
    }

    private static string ExtensionForContentType(string contentType)
    {
        return (contentType ?? string.Empty).ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "application/msword" => ".doc",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            _ => string.Empty,
        };
    }
}
