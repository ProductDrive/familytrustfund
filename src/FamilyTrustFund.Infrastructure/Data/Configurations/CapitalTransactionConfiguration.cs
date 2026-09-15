using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Payments;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTrustFund.Infrastructure.Data.Configurations;

public class CapitalTransactionConfiguration : IEntityTypeConfiguration<CapitalTransaction>
{
    public void Configure(EntityTypeBuilder<CapitalTransaction> builder)
    {
        builder.ToTable("capital_transactions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.FundId).IsRequired();
        builder.Property(t => t.GuarantorId).IsRequired();
        builder.Property(t => t.Provider).HasMaxLength(50).IsRequired();
        builder.Property(t => t.AmountGross).HasPrecision(18, 2).IsRequired();
        builder.Property(t => t.ProviderFee).HasPrecision(18, 2);
        builder.Property(t => t.AmountNet).HasPrecision(18, 2);
        builder.Property(t => t.ProviderReference).HasMaxLength(100).IsRequired();
        builder.Property(t => t.LastProcessedEventId).HasMaxLength(200);
        builder.Property(t => t.Status).HasConversion<int>().IsRequired();
        builder.Property(t => t.FailureReason).HasMaxLength(1000);
        builder.Property(t => t.InitiatedAtUtc).IsRequired();
        builder.Property(t => t.UpdatedAtUtc).IsRequired();

        builder.HasIndex(t => t.FundId);
        builder.HasIndex(t => t.ProviderReference).IsUnique();
        builder.HasIndex(t => t.LastProcessedEventId);
        builder.HasIndex(t => t.Status);

        builder.HasOne<Fund>()
            .WithMany()
            .HasForeignKey(t => t.FundId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(t => t.GuarantorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}