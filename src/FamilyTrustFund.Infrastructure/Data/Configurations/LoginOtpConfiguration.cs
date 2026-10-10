using FamilyTrustFund.Domain.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTrustFund.Infrastructure.Data.Configurations;

public class LoginOtpConfiguration : IEntityTypeConfiguration<LoginOtp>
{
    public void Configure(EntityTypeBuilder<LoginOtp> builder)
    {
        builder.ToTable("login_otp");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Email).HasMaxLength(256).IsRequired();
        builder.Property(o => o.RequestedRole).HasMaxLength(50).IsRequired();
        builder.Property(o => o.TermsVersion).HasMaxLength(50);
        builder.Property(o => o.CodeHash).HasMaxLength(128).IsRequired();
        builder.Property(o => o.CodeSalt).HasMaxLength(128).IsRequired();
        builder.Property(o => o.ExpiresAtUtc).IsRequired();
        builder.Property(o => o.AttemptCount).IsRequired();
        builder.Property(o => o.MaxAttempts).IsRequired();
        builder.Property(o => o.CreatedAtUtc).IsRequired();

        builder.HasIndex(o => o.Email);
        builder.HasIndex(o => new { o.Email, o.ConsumedAtUtc });
    }
}