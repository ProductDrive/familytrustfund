using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTrustFund.Infrastructure.Data.Configurations;

public class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        builder.ToTable("loans");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.FundId)
            .IsRequired();

        builder.Property(l => l.MemberId)
            .IsRequired();

        builder.Property(l => l.GuarantorId);

        builder.Property(l => l.RequestedAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(l => l.ApprovedAmount)
            .HasPrecision(18, 2);

        builder.Property(l => l.InterestRate)
            .HasPrecision(18, 6)
            .IsRequired();

        builder.Property(l => l.RequestedFrequency)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(l => l.ApprovedFrequency)
            .HasConversion<int>();

        builder.Property(l => l.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(l => l.FundingSource)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(l => l.OutstandingBalance)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(l => l.TotalRepayable)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(l => l.Purpose)
            .HasMaxLength(1000);

        builder.Property(l => l.RejectionReason)
            .HasMaxLength(1000);

        builder.Property(l => l.RequestedAtUtc).IsRequired();
        builder.Property(l => l.CreatedAtUtc).IsRequired();
        builder.Property(l => l.UpdatedAtUtc).IsRequired();

        // Indexes for common query patterns.
        builder.HasIndex(l => l.FundId);
        builder.HasIndex(l => l.MemberId);
        builder.HasIndex(l => l.Status);
        builder.HasIndex(l => new { l.FundId, l.MemberId });
        builder.HasIndex(l => new { l.FundId, l.Status });
        builder.HasIndex(l => new { l.MemberId, l.Status });

        // FK to Fund (Cascade delete).
        builder.HasOne<FamilyTrustFund.Domain.Funds.Fund>()
            .WithMany()
            .HasForeignKey(l => l.FundId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK to Member (Restrict — don't delete a user who has loans).
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(l => l.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK to Guarantor (Restrict).
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(l => l.GuarantorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
