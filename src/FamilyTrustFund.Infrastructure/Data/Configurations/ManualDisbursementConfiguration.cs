using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Payments;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTrustFund.Infrastructure.Data.Configurations;

public class ManualDisbursementConfiguration : IEntityTypeConfiguration<ManualDisbursement>
{
    public void Configure(EntityTypeBuilder<ManualDisbursement> builder)
    {
        builder.ToTable("manual_disbursements");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.LoanId).IsRequired();
        builder.Property(r => r.FundId).IsRequired();
        builder.Property(r => r.MemberId).IsRequired();
        builder.Property(r => r.ReportedByGuarantorId).IsRequired();
        builder.Property(r => r.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.Method).IsRequired();
        builder.Property(r => r.DestinationBankName).HasMaxLength(200);
        builder.Property(r => r.DestinationAccountLast4).HasMaxLength(4);
        builder.Property(r => r.Reference).HasMaxLength(200);
        builder.Property(r => r.Note).HasMaxLength(1000);
        builder.Property(r => r.Status).IsRequired();
        builder.Property(r => r.MemberNote).HasMaxLength(1000);
        builder.Property(r => r.CancelReason).HasMaxLength(1000);
        builder.Property(r => r.ReportedAtUtc).IsRequired();

        // Optimistic concurrency: a member confirmation racing a Guarantor
        // cancellation cannot post the loan twice (ADR-056).
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasIndex(r => r.LoanId);
        builder.HasIndex(r => new { r.MemberId, r.Status });
        builder.HasIndex(r => new { r.FundId, r.Status });

        builder.HasOne<Loan>()
            .WithMany()
            .HasForeignKey(r => r.LoanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Fund>()
            .WithMany()
            .HasForeignKey(r => r.FundId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.ReportedByGuarantorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
