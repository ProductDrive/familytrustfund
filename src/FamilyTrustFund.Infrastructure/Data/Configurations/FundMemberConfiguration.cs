using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Membership;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTrustFund.Infrastructure.Data.Configurations;

public class FundMemberConfiguration : IEntityTypeConfiguration<FundMember>
{
    public void Configure(EntityTypeBuilder<FundMember> builder)
    {
        builder.ToTable("fund_members");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.FundId)
            .IsRequired();

        builder.Property(m => m.MemberId)
            .IsRequired();

        builder.Property(m => m.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(m => m.JoinedAtUtc).IsRequired();
        builder.Property(m => m.UpdatedAtUtc).IsRequired();

        builder.HasIndex(m => new { m.FundId, m.MemberId }).IsUnique();
        builder.HasIndex(m => m.MemberId);
        builder.HasIndex(m => m.FundId);

        builder.HasOne<Fund>()
            .WithMany()
            .HasForeignKey(m => m.FundId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(m => m.MemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}