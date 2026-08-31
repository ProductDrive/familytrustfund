using FamilyTrustFund.Application.Audit;
using FamilyTrustFund.Infrastructure.Data;

namespace FamilyTrustFund.Infrastructure.Audit;

/// <summary>
/// Writes audit events into the ambient <see cref="ApplicationDbContext"/>.
/// Deliberately does not save on its own: the caller's transaction commit
/// persists the audit record together with the surrounding business change.
/// </summary>
public class AuditLog : IAuditLog
{
    private readonly ApplicationDbContext _db;

    public AuditLog(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task RecordAsync(
        Guid actorId,
        string action,
        string resourceType,
        Guid? resourceId = null,
        string? details = null,
        CancellationToken ct = default)
    {
        _db.AuditEvents.Add(AuditEvent.Create(actorId, action, resourceType, resourceId, details));
        return Task.CompletedTask;
    }
}