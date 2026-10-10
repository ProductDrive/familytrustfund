using FamilyTrustFund.Domain.Auth;
using Microsoft.AspNetCore.Identity;

namespace FamilyTrustFund.Infrastructure.Identity;

/// <summary>
/// Application user managed by ASP.NET Core Identity.
/// Owns session/authentication concerns and the user's profile display name.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Guarantor self-registration state. Regular users stay
    /// <see cref="GuarantorApprovalStatus.NotRequested"/>; a self-registered
    /// Guarantor is <see cref="GuarantorApprovalStatus.Pending"/> and does not
    /// receive the Guarantor role until approved.
    /// </summary>
    public GuarantorApprovalStatus GuarantorApprovalStatus { get; set; } = GuarantorApprovalStatus.NotRequested;
}
