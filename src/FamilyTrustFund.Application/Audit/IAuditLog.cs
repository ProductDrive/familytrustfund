namespace FamilyTrustFund.Application.Audit;

/// <summary>
/// Port for writing audit records. Implemented by the infrastructure layer.
/// Audit records must not contain secrets, full bank details or unnecessary
/// personal/financial data.
/// </summary>
public interface IAuditLog
{
    /// <summary>
    /// Records an audit event. The entry is written to the ambient
    /// persistence context so committing the surrounding financial operation
    /// also commits the audit record atomically.
    /// </summary>
    Task RecordAsync(
        Guid actorId,
        string action,
        string resourceType,
        Guid? resourceId = null,
        string? details = null,
        CancellationToken ct = default);
}