namespace FamilyTrustFund.Infrastructure.Storage;

/// <summary>
/// Configuration for filesystem-backed object storage. Read from
/// configuration, e.g. "Storage:FileSystem:RootPath".
/// </summary>
public sealed class FileSystemStorageOptions
{
    public const string SectionName = "Storage:FileSystem";

    /// <summary>
    /// Root directory under which container directories are created. Content is
    /// stored outside the web root (AGENTS §9). Defaults to a platform temp
    /// folder if not configured.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;
}
