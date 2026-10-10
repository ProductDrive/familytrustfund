using System.Security.Cryptography;

namespace FamilyTrustFund.Domain.Auth;

/// <summary>
/// A single one-time code used for passwordless email sign-in. Codes are never
/// stored in plain text: only an HMAC/SHA-256 hash plus the random salt used to
/// derive it are persisted (AGENTS §7 financial/sensitive data handling).
/// </summary>
public class LoginOtp
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Email { get; private set; } = string.Empty;

    /// <summary>
    /// Role the requester selected at sign-in. Server-authoritative at
    /// verification time; for an existing account the stored roles win and this
    /// value is ignored (never an escalation path).
    /// </summary>
    public string RequestedRole { get; private set; } = string.Empty;

    /// <summary>
    /// Terms version accepted at request time when the requester selected the
    /// Guarantor role; null otherwise.
    /// </summary>
    public string? TermsVersion { get; private set; }

    public string CodeHash { get; private set; } = string.Empty;

    public string CodeSalt { get; private set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; private set; }

    public int AttemptCount { get; private set; }

    public int MaxAttempts { get; private set; }

    public DateTime? ConsumedAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    protected LoginOtp() { }

    public static LoginOtp Issue(
        string normalizedEmail,
        string requestedRole,
        string? termsVersion,
        string codeHash,
        string codeSalt,
        DateTime expiresAtUtc,
        int maxAttempts) => new()
    {
        Email = normalizedEmail,
        RequestedRole = requestedRole,
        TermsVersion = termsVersion,
        CodeHash = codeHash,
        CodeSalt = codeSalt,
        ExpiresAtUtc = expiresAtUtc,
        MaxAttempts = maxAttempts,
        AttemptCount = 0,
    };

    public bool IsConsumed => ConsumedAtUtc.HasValue;

    public bool IsExpiredAt(DateTime utcNow) => utcNow > ExpiresAtUtc;

    public bool HasExceededAttempts => AttemptCount >= MaxAttempts;

    public bool MatchesCode(string codeHash)
    {
        if (string.IsNullOrEmpty(CodeHash) || string.IsNullOrEmpty(codeHash))
        {
            return false;
        }

        var expected = Convert.FromBase64String(CodeHash);
        var actual = Convert.FromBase64String(codeHash);
        return expected.Length == actual.Length
            && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public void RecordFailedAttempt() => AttemptCount++;

    public void Consume(DateTime utcNow) => ConsumedAtUtc = utcNow;
}