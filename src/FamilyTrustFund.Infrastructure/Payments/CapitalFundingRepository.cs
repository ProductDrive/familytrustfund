using FamilyTrustFund.Application.Payments;
using FamilyTrustFund.Domain.Payments;
using FamilyTrustFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyTrustFund.Infrastructure.Payments;

public class CapitalFundingRepository : ICapitalFundingRepository
{
    private readonly ApplicationDbContext _db;

    public CapitalFundingRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<CapitalTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.CapitalTransactions.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<CapitalTransaction?> GetByProviderReferenceAsync(
        string providerReference,
        CancellationToken ct = default) =>
        _db.CapitalTransactions.FirstOrDefaultAsync(t => t.ProviderReference == providerReference, ct);

    public async Task<IReadOnlyList<CapitalTransaction>> GetByFundAsync(
        Guid fundId,
        int page,
        int pageSize,
        CancellationToken ct = default) =>
        await _db.CapitalTransactions
            .Where(t => t.FundId == fundId)
            .OrderByDescending(t => t.InitiatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

    public Task<int> CountByFundAsync(Guid fundId, CancellationToken ct = default) =>
        _db.CapitalTransactions.CountAsync(t => t.FundId == fundId, ct);

    public Task<decimal> SumConfirmedNetByFundAsync(Guid fundId, CancellationToken ct = default) =>
        _db.CapitalTransactions
            .Where(t => t.FundId == fundId && t.Status == CapitalTransactionStatus.Confirmed)
            .SumAsync(t => t.AmountNet ?? 0m, ct);

    public void Add(CapitalTransaction transaction) => _db.CapitalTransactions.Add(transaction);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}