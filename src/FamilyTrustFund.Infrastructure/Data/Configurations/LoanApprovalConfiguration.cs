using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTrustFund.Infrastructure.Data.Configurations;

public class LoanApprovalConfiguration : IEntityTypeConfiguration<LoanApproval>
{
    public void Configure(EntityTypeBuilder<LoanApproval> builder)
    {
        builder.ToTable("loan_approvals");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.LoanId).IsRequired();
        builder.Property(a => a.GuarantorId).IsRequired();

        builder.Property(a => a.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(a => a.DecidedAtUtc);
        builder.Property(a => a.Comment).HasMaxLength(1000);
        builder.Property(a => a.CreatedAtUtc).IsRequired();

        // A Guarantor can only ever have one decision row per loan.
        builder.HasIndex(a => new { a.LoanId, a.GuarantorId }).IsUnique();
        builder.HasIndex(a => new { a.GuarantorId, a.Status });

        builder.HasOne<Loan>()
            .WithMany()
            .HasForeignKey(a => a.LoanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.GuarantorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
