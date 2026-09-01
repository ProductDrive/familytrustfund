using FamilyTrustFund.Domain.Payments;

namespace FamilyTrustFund.Application.Payments;

/// <summary>
/// Persistence port for payment recipients and disbursement transactions.
/// </summary>
public interface IPaymentRepository
{
    Task<PaymentRecipient?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PaymentRecipient?> GetActiveRecipientForMemberAsync(Guid memberId, CancellationToken ct = default);

    Task<PaymentRecipient?> GetLatestRecipientForMemberAsync(Guid memberId, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentRecipient>> GetRecipientsByMemberAsync(Guid memberId, CancellationToken ct = default);

    Task<DisbursementTransaction?> GetByLoanAsync(Guid loanId, CancellationToken ct = default);

    Task<DisbursementTransaction?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);

    Task<DisbursementTransaction?> GetByProviderReferenceAsync(string providerReference, CancellationToken ct = default);

    Task<bool> HasProcessedEventAsync(string eventId, CancellationToken ct = default);

    void AddRecipient(PaymentRecipient recipient);
    void AddDisbursement(DisbursementTransaction transaction);
    Task SaveChangesAsync(CancellationToken ct = default);
}
