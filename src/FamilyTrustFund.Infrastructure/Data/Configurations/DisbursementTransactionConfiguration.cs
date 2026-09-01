using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTrustFund.Infrastructure.Data.Configurations;

public class DisbursementTransactionConfiguration : IEntityTypeConfiguration<DisbursementTransaction>
{
    public void Configure(EntityTypeBuilder<DisbursementTransaction> builder)
    {
        builder.ToTable("disbursement_transactions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.LoanId).IsRequired();
        builder.Property(t => t.RecipientId).IsRequired();
        builder.Property(t => t.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(t => t.Currency).HasMaxLength(8).IsRequired();
        builder.Property(t => t.ProviderReference).HasMaxLength(100).IsRequired();
        builder.Property(t => t.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(t => t.LastProcessedEventId).HasMaxLength(200);
        builder.Property(t => t.Status).HasConversion<int>().IsRequired();
        builder.Property(t => t.FailureReason).HasMaxLength(1000);
        builder.Property(t => t.InitiatedAtUtc).IsRequired();
        builder.Property(t => t.UpdatedAtUtc).IsRequired();

        builder.HasIndex(t => t.LoanId).IsUnique();
        builder.HasIndex(t => t.IdempotencyKey).IsUnique();
        builder.HasIndex(t => t.ProviderReference).IsUnique();
        builder.HasIndex(t => t.LastProcessedEventId);
        builder.HasIndex(t => t.Status);

        builder.HasOne<Loan>()
            .WithMany()
            .HasForeignKey(t => t.LoanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PaymentRecipient>()
            .WithMany()
            .HasForeignKey(t => t.RecipientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
