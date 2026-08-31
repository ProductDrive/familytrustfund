using FamilyTrustFund.Domain.Auth;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FamilyTrustFund.Infrastructure.Data;

/// <summary>
/// Seeds the well-known roles and (in development only) a default
/// SuperAdmin account. Idempotent and safe to run on every startup.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();

        foreach (var roleName in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new ApplicationRole(roleName));
            }
        }

        var environment = services.GetRequiredService<IHostEnvironment>();
        var seedSuperAdmin = services.GetRequiredService<IConfiguration>()["Seed:SuperAdmin"];
        if ((environment.IsDevelopment() || environment.IsStaging()) && !string.IsNullOrWhiteSpace(seedSuperAdmin))
        {
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var email = seedSuperAdmin?.Trim().ToLowerInvariant();
            if (email != null && !string.IsNullOrWhiteSpace(email) && email.Contains('@'))
            {
                var admin = await userManager.FindByEmailAsync(email);
                if (admin == null)
                {
                    admin = new ApplicationUser
                    {
                        UserName = email,
                        Email = email,
                        DisplayName = "Super Admin",
                        EmailConfirmed = true,
                    };
                    var result = await userManager.CreateAsync(admin);
                    if (result.Succeeded)
                    {
                        await userManager.AddToRoleAsync(admin, AppRoles.SuperAdmin);
                    }
                }
            }
        }
    }
}
