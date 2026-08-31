using FamilyTrustFund.Infrastructure.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTrustFund.Infrastructure.Data.Configurations;

public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ActorId).IsRequired();
        builder.Property(e => e.Action).HasMaxLength(120).IsRequired();
        builder.Property(e => e.ResourceType).HasMaxLength(80).IsRequired();
        builder.Property(e => e.Details).HasMaxLength(2000);
        builder.Property(e => e.CreatedAtUtc).IsRequired();

        builder.HasIndex(e => new { e.ResourceType, e.ResourceId });
        builder.HasIndex(e => e.ActorId);
        builder.HasIndex(e => e.Action);
        builder.HasIndex(e => e.CreatedAtUtc);
    }
}