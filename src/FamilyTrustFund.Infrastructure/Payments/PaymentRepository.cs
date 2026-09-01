using FamilyTrustFund.Application.Payments;
using FamilyTrustFund.Domain.Payments;
using FamilyTrustFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyTrustFund.Infrastructure.Payments;

public class PaymentRepository : IPaymentRepository
{
    private readonly ApplicationDbContext _db;

    public PaymentRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<PaymentRecipient?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.PaymentRecipients.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<PaymentRecipient?> GetActiveRecipientForMemberAsync(
        Guid memberId,
        CancellationToken ct = default) =>
        _db.PaymentRecipients
            .Where(r => r.MemberId == memberId && r.IsActive)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    public Task<PaymentRecipient?> GetLatestRecipientForMemberAsync(
        Guid memberId,
        CancellationToken ct = default) =>
        _db.PaymentRecipients
            .Where(r => r.MemberId == memberId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<PaymentRecipient>> GetRecipientsByMemberAsync(
        Guid memberId,
        CancellationToken ct = default) =>
        await _db.PaymentRecipients
            .Where(r => r.MemberId == memberId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

    public Task<DisbursementTransaction?> GetByLoanAsync(Guid loanId, CancellationToken ct = default) =>
        _db.DisbursementTransactions.FirstOrDefaultAsync(t => t.LoanId == loanId, ct);

    public Task<DisbursementTransaction?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken ct = default) =>
        _db.DisbursementTransactions.FirstOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey, ct);

    public Task<DisbursementTransaction?> GetByProviderReferenceAsync(
        string providerReference,
        CancellationToken ct = default) =>
        _db.DisbursementTransactions.FirstOrDefaultAsync(t => t.ProviderReference == providerReference, ct);

    public Task<bool> HasProcessedEventAsync(string eventId, CancellationToken ct = default) =>
        _db.DisbursementTransactions.AnyAsync(t => t.LastProcessedEventId == eventId, ct);

    public void AddRecipient(PaymentRecipient recipient) => _db.PaymentRecipients.Add(recipient);

    public void AddDisbursement(DisbursementTransaction transaction) => _db.DisbursementTransactions.Add(transaction);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
