using FamilyTrustFund.Domain.Payments;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTrustFund.Infrastructure.Data.Configurations;

public class PaymentRecipientConfiguration : IEntityTypeConfiguration<PaymentRecipient>
{
    public void Configure(EntityTypeBuilder<PaymentRecipient> builder)
    {
        builder.ToTable("payment_recipients");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.MemberId).IsRequired();
        builder.Property(r => r.Provider).HasMaxLength(50).IsRequired();
        builder.Property(r => r.BankCode).HasMaxLength(20).IsRequired();
        builder.Property(r => r.BankName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.AccountNumber).HasMaxLength(20).IsRequired();
        builder.Property(r => r.AccountName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.ProviderRecipientCode).HasMaxLength(100);
        builder.Property(r => r.Status).HasConversion<int>().IsRequired();
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.UpdatedAtUtc).IsRequired();

        builder.HasIndex(r => r.MemberId);
        builder.HasIndex(r => new { r.MemberId, r.IsActive });

        // Restrict: keep historical recipients even if the user record is managed separately.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.MemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
