namespace FamilyTrustFund.Infrastructure.Audit;

/// <summary>
/// Immutable record of a material business action, for accountability.
/// </summary>
public class AuditEvent
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ActorId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string ResourceType { get; private set; } = string.Empty;
    public Guid? ResourceId { get; private set; }
    public string? Details { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    protected AuditEvent() { }

    public static AuditEvent Create(
        Guid actorId,
        string action,
        string resourceType,
        Guid? resourceId,
        string? details) => new()
    {
        ActorId = actorId,
        Action = action,
        ResourceType = resourceType,
        ResourceId = resourceId,
        Details = details,
    };
}