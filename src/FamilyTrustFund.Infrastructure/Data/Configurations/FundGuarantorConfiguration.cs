using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTrustFund.Infrastructure.Data.Configurations;

public class FundGuarantorConfiguration : IEntityTypeConfiguration<FundGuarantor>
{
    public void Configure(EntityTypeBuilder<FundGuarantor> builder)
    {
        builder.ToTable("fund_guarantors");

        builder.HasKey(g => g.Id);

        builder.Property(g => g.FundId).IsRequired();
        builder.Property(g => g.GuarantorId).IsRequired();

        builder.Property(g => g.Role)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(g => g.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(g => g.CommittedCapital)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(g => g.InvitedEmail)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(g => g.InvitedByUserId).IsRequired();

        builder.Property(g => g.InvitedAtUtc).IsRequired();
        builder.Property(g => g.RespondedAtUtc);
        builder.Property(g => g.ExpiresAtUtc);
        builder.Property(g => g.CreatedAtUtc).IsRequired();
        builder.Property(g => g.UpdatedAtUtc).IsRequired();

        // One relationship per Guarantor per fund: prevents duplicate
        // invitations and double joins (ADR-051).
        builder.HasIndex(g => new { g.FundId, g.GuarantorId }).IsUnique();
        builder.HasIndex(g => new { g.GuarantorId, g.Status });
        builder.HasIndex(g => new { g.FundId, g.Status });

        builder.HasOne<Fund>()
            .WithMany()
            .HasForeignKey(g => g.FundId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(g => g.GuarantorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(g => g.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
