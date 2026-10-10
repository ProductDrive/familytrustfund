using FamilyTrustFund.Domain.Auth;

namespace FamilyTrustFund.Application.Auth;

/// <summary>
/// Persistence port for passwordless sign-in codes. Implemented by the
/// infrastructure layer.
/// </summary>
public interface ILoginOtpRepository
{
    /// <summary>Most recent code issued for an email, in any state.</summary>
    Task<LoginOtp?> GetLatestByEmailAsync(string normalizedEmail, CancellationToken ct = default);

    /// <summary>Most recent not-yet-consumed code for an email.</summary>
    Task<LoginOtp?> GetLatestUnconsumedAsync(string normalizedEmail, CancellationToken ct = default);

    /// <summary>All not-yet-consumed codes for an email.</summary>
    Task<IReadOnlyList<LoginOtp>> GetUnconsumedByEmailAsync(string normalizedEmail, CancellationToken ct = default);

    void Add(LoginOtp otp);

    void RemoveRange(IEnumerable<LoginOtp> otps);

    Task SaveChangesAsync(CancellationToken ct = default);
}