using FamilyTrustFund.Application.Auth;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace FamilyTrustFund.Infrastructure.Auth;

public class UserAccountLookup(UserManager<ApplicationUser> userManager) : IUserAccountLookup
{
    public async Task<bool> ExistsAsync(string email, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        return user is not null;
    }
}
