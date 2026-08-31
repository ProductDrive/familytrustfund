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
}
