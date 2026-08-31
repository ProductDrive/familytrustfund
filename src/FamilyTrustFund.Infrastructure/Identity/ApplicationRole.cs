using Microsoft.AspNetCore.Identity;

namespace FamilyTrustFund.Infrastructure.Identity;

/// <summary>
/// Application role managed by ASP.NET Core Identity.
/// </summary>
public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole(string name) : base(name) { }
}
