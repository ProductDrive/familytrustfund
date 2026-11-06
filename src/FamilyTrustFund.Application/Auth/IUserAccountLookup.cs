namespace FamilyTrustFund.Application.Auth;

/// <summary>
/// Read-only lookup used while issuing sign-in codes. Terms acceptance is a
/// sign-up concern only, so the OTP service must know whether the email already
/// belongs to an account. Implemented by the infrastructure layer (Identity).
/// </summary>
public interface IUserAccountLookup
{
    /// <summary>True when an application account already uses this email.</summary>
    Task<bool> ExistsAsync(string email, CancellationToken ct = default);
}
