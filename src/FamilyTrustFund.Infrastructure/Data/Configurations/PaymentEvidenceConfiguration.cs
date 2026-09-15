using FamilyTrustFund.Domain.Evidence;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTrustFund.Infrastructure.Data.Configurations;

public class PaymentEvidenceConfiguration : IEntityTypeConfiguration<PaymentEvidence>
{
    public void Configure(EntityTypeBuilder<PaymentEvidence> builder)
    {
        builder.ToTable("payment_evidence");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.UploadedByUserId).IsRequired();
        builder.Property(e => e.ResourceType).HasMaxLength(40).IsRequired();
        builder.Property(e => e.ResourceId).IsRequired();
        builder.Property(e => e.StorageContainer).HasMaxLength(80).IsRequired();
        builder.Property(e => e.StorageKey).HasMaxLength(300).IsRequired();
        builder.Property(e => e.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(e => e.SizeBytes).IsRequired();

        builder.Property(e => e.UploadedAtUtc).IsRequired();

        // A resource may have many evidence attachments; lookups are by the
        // owning financial resource.
        builder.HasIndex(e => new { e.ResourceType, e.ResourceId });
        builder.HasIndex(e => e.UploadedByUserId);

        // FK to uploader user (Restrict — never cascade delete a user's evidence).
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
