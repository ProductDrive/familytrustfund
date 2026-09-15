using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Repayments;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTrustFund.Infrastructure.Data.Configurations;

public class PendingRepaymentConfiguration : IEntityTypeConfiguration<PendingRepayment>
{
    public void Configure(EntityTypeBuilder<PendingRepayment> builder)
    {
        builder.ToTable("pending_repayment");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.LoanId).IsRequired();
        builder.Property(p => p.MemberId).IsRequired();
        builder.Property(p => p.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.Kind).IsRequired();
        builder.Property(p => p.Reference).HasMaxLength(200);
        builder.Property(p => p.Note).HasMaxLength(1000);
        builder.Property(p => p.Status).IsRequired();
        builder.Property(p => p.RejectionReason).HasMaxLength(1000);
        builder.Property(p => p.ReportedAtUtc).IsRequired();

        builder.HasIndex(p => p.LoanId);
        builder.HasIndex(p => new { p.MemberId, p.Status });

        builder.HasOne<Loan>()
            .WithMany()
            .HasForeignKey(p => p.LoanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.MemberId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
