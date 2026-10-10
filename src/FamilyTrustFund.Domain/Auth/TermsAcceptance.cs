namespace FamilyTrustFund.Domain.Auth;

/// <summary>
/// Record of a user accepting the current Guarantor terms version. Immutable,
/// versioned, timestamped: the acceptance is a legal/audit record, never
/// overwritten (AGENTS §13).
/// </summary>
public class TermsAcceptance
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid UserId { get; private set; }

    public string Version { get; private set; } = string.Empty;

    public DateTime AcceptedAtUtc { get; private set; } = DateTime.UtcNow;

    protected TermsAcceptance() { }

    public static TermsAcceptance Create(Guid userId, string version) => new()
    {
        UserId = userId,
        Version = version,
    };
}