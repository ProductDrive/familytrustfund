namespace FamilyTrustFund.Application.Storage;

/// <summary>
/// Raised when a file cannot be stored or retrieved.
/// </summary>
public sealed class StorageException : Exception
{
    public StorageException(string message) : base(message)
    {
    }
}
