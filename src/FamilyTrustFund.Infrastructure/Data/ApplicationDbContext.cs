using FamilyTrustFund.Domain.Contributions;
using FamilyTrustFund.Domain.Evidence;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Membership;
using FamilyTrustFund.Domain.Payments;
using FamilyTrustFund.Domain.Repayments;
using FamilyTrustFund.Infrastructure.Audit;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FamilyTrustFund.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<
    ApplicationUser,
    ApplicationRole,
    Guid,
    IdentityUserClaim<Guid>,
    IdentityUserRole<Guid>,
    IdentityUserLogin<Guid>,
    IdentityRoleClaim<Guid>,
    IdentityUserToken<Guid>>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Fund> Funds => Set<Fund>();
    public DbSet<FundMember> FundMembers => Set<FundMember>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<FundContribution> FundContributions => Set<FundContribution>();
    public DbSet<PaymentRecipient> PaymentRecipients => Set<PaymentRecipient>();
    public DbSet<DisbursementTransaction> DisbursementTransactions => Set<DisbursementTransaction>();
    public DbSet<CapitalTransaction> CapitalTransactions => Set<CapitalTransaction>();
    public DbSet<LoanSchedule> LoanSchedules => Set<LoanSchedule>();
    public DbSet<LoanScheduleItem> LoanScheduleItems => Set<LoanScheduleItem>();
    public DbSet<Repayment> Repayments => Set<Repayment>();
    public DbSet<PendingRepayment> PendingRepayments => Set<PendingRepayment>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<PaymentEvidence> PaymentEvidences => Set<PaymentEvidence>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.DisplayName).HasMaxLength(200);
            e.HasIndex(u => u.Email).IsUnique();
        });
    }
}
