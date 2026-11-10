using FamilyTrustFund.Domain.Contributions;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTrustFund.Infrastructure.Data.Configurations;

public class FundContributionConfiguration : IEntityTypeConfiguration<FundContribution>
{
    public void Configure(EntityTypeBuilder<FundContribution> builder)
    {
        builder.ToTable("fund_contributions");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.FundId).IsRequired();
        builder.Property(c => c.MemberId).IsRequired();

        builder.Property(c => c.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(c => c.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(c => c.Reference).HasMaxLength(200);
        builder.Property(c => c.Note).HasMaxLength(1000);
        builder.Property(c => c.RejectionReason).HasMaxLength(1000);
        builder.Property(c => c.ConfirmationNote).HasMaxLength(1000);
        builder.Property(c => c.ConfirmedByUserId);
        builder.Property(c => c.RejectedByUserId);

        builder.Property(c => c.ReportedAtUtc).IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.UpdatedAtUtc).IsRequired();

        // Optimistic concurrency: two Group Guarantors must not both confirm the
        // same contribution (ADR-054).
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Indexes for common query patterns.
        builder.HasIndex(c => c.FundId);
        builder.HasIndex(c => c.MemberId);
        builder.HasIndex(c => c.Status);
        builder.HasIndex(c => new { c.FundId, c.MemberId });
        builder.HasIndex(c => new { c.FundId, c.Status });

        // FK to Fund (Cascade delete).
        builder.HasOne<FamilyTrustFund.Domain.Funds.Fund>()
            .WithMany()
            .HasForeignKey(c => c.FundId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK to Member (Restrict — don't delete a user who has contributions).
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(c => c.MemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
