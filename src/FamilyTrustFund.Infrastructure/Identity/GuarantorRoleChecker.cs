using FamilyTrustFund.Application.Membership;
using FamilyTrustFund.Domain.Auth;
using Microsoft.AspNetCore.Identity;

namespace FamilyTrustFund.Infrastructure.Identity;

public class GuarantorRoleChecker(UserManager<ApplicationUser> userManager) : IGuarantorRoleChecker
{
    public async Task<bool> IsGuarantorAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return false;
        return await userManager.IsInRoleAsync(user, AppRoles.Guarantor);
    }
}
