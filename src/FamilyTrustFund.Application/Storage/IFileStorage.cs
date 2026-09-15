namespace FamilyTrustFund.Application.Storage;

/// <summary>
/// Port for storing and retrieving object content (e.g. payment evidence files).
/// </summary>
/// <remarks>
/// The domain/application depends on this abstraction, never on a concrete
/// provider. The initial implementation stores files on the VPS filesystem;
/// a future object-storage provider (e.g. Google Cloud Storage) can be added
/// without touching domain logic (AGENTS §5, §9).
///
/// Objects are addressed by server-generated keys. Callers must never use a
/// user-supplied filename as a key. Content is opaque bytes; validation of type
/// and size is the caller's responsibility (enforced at the evidence layer).
/// </remarks>
public interface IFileStorage
{
    /// <summary>
    /// Stores the given content under a server-generated key.
    /// </summary>
    Task<StoredObject> StoreAsync(string container, byte[] content, string contentType, CancellationToken ct = default);

    /// <summary>
    /// Reads the content for a previously stored object key. Returns null when
    /// the object does not exist.
    /// </summary>
    Task<StoredObject?> ReadAsync(string container, string objectKey, CancellationToken ct = default);

    /// <summary>
    /// Deletes an object. Returns true if it existed and was removed.
    /// </summary>
    Task<bool> DeleteAsync(string container, string objectKey, CancellationToken ct = default);
}

/// <summary>
/// Describes stored content and provides the bytes.
/// </summary>
public sealed class StoredObject
{
    public string Container { get; init; } = string.Empty;
    public string ObjectKey { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public byte[] Content { get; init; } = Array.Empty<byte>();
}
