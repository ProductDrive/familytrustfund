using FamilyTrustFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FamilyTrustFund.Infrastructure;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> to create the context for
/// generating and applying migrations without running the application.
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        var connectionString = args.Length > 0
            ? args[0]
            : Environment.GetEnvironmentVariable("CONNECTION_STRING")
              ?? "Host=localhost;Port=5432;Database=familytrustfund;Username=postgres;Password=postgres";

        optionsBuilder.UseNpgsql(connectionString);

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
