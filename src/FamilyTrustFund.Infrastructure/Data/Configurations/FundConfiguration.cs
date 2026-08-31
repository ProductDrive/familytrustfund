using FamilyTrustFund.Domain.Funds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTrustFund.Infrastructure.Data.Configurations;

public class FundConfiguration : IEntityTypeConfiguration<Fund>
{
    public void Configure(EntityTypeBuilder<Fund> builder)
    {
        builder.ToTable("funds");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Name)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(f => f.GuarantorId)
            .IsRequired();

        builder.Property(f => f.JoinCode)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(f => f.Type)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(f => f.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(f => f.CommittedCapital)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(f => f.ContributionMultiplier)
            .HasPrecision(18, 6)
            .IsRequired();

        builder.Property(f => f.InterestRate)
            .HasPrecision(18, 6);

        builder.Property(f => f.HowItWorks)
            .HasMaxLength(4000);

        builder.Property(f => f.CreatedAtUtc).IsRequired();
        builder.Property(f => f.UpdatedAtUtc).IsRequired();

        builder.HasIndex(f => new { f.GuarantorId, f.JoinCode }).IsUnique();
        builder.HasIndex(f => f.GuarantorId);
    }
}
