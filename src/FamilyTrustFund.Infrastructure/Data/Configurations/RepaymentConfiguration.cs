using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Repayments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTrustFund.Infrastructure.Data.Configurations;

public class LoanScheduleConfiguration : IEntityTypeConfiguration<LoanSchedule>
{
    public void Configure(EntityTypeBuilder<LoanSchedule> builder)
    {
        builder.ToTable("loan_schedules");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.LoanId).IsRequired();
        builder.Property(s => s.Version).IsRequired();
        builder.Property(s => s.CreatedAtUtc).IsRequired();

        builder.HasIndex(s => new { s.LoanId, s.Version }).IsUnique(true);
        builder.HasIndex(s => s.LoanId);

        builder.Metadata.FindNavigation(nameof(LoanSchedule.Items))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(s => s.Items)
            .WithOne()
            .HasForeignKey(i => i.ScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Loan>()
            .WithMany()
            .HasForeignKey(s => s.LoanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class LoanScheduleItemConfiguration : IEntityTypeConfiguration<LoanScheduleItem>
{
    public void Configure(EntityTypeBuilder<LoanScheduleItem> builder)
    {
        builder.ToTable("loan_schedule_items");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.ScheduleId).IsRequired();
        builder.Property(i => i.Sequence).IsRequired();
        builder.Property(i => i.DueDateUtc).IsRequired();
        builder.Property(i => i.PrincipalDue).HasPrecision(18, 2).IsRequired();
        builder.Property(i => i.InterestDue).HasPrecision(18, 2).IsRequired();
        builder.Property(i => i.ExpectedAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(i => i.PaidAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(i => i.PaidAtUtc);
        builder.Property(i => i.Status).HasConversion<int>().IsRequired();

        builder.HasIndex(i => i.ScheduleId);
        builder.HasIndex(i => i.Status);
    }
}

public class RepaymentConfiguration : IEntityTypeConfiguration<Repayment>
{
    public void Configure(EntityTypeBuilder<Repayment> builder)
    {
        builder.ToTable("repayments");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.LoanId).IsRequired();
        builder.Property(r => r.ScheduleVersion).IsRequired();
        builder.Property(r => r.Kind).HasConversion<int>().IsRequired();
        builder.Property(r => r.ExpectedAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.ActualAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.Surplus).HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.PaidAtUtc).IsRequired();
        builder.Property(r => r.Note).HasMaxLength(500);

        builder.HasIndex(r => r.LoanId);

        builder.HasOne<Loan>()
            .WithMany()
            .HasForeignKey(r => r.LoanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
