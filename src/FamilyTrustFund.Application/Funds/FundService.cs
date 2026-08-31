using FamilyTrustFund.Application.Audit;
using FamilyTrustFund.Domain.Funds;

namespace FamilyTrustFund.Application.Funds;

/// <summary>
/// Application service for fund lifecycle operations. All financial values are
/// computed and validated server-side; clients only supply intent.
/// </summary>
public class FundService
{
    private readonly IFundRepository _repository;
    private readonly IAuditLog _auditLog;

    public FundService(IFundRepository repository, IAuditLog auditLog)
    {
        _repository = repository;
        _auditLog = auditLog;
    }

    public async Task<Fund> CreateFundAsync(
        Guid guarantorId,
        CreateFundRequest request,
        CancellationToken ct = default)
    {
        var joinCode = request.JoinCode;

        if (string.IsNullOrWhiteSpace(joinCode))
        {
            // Generate until we find one that is unique for this Guarantor.
            do
            {
                joinCode = JoinCodeGenerator.Generate();
            } while (await _repository.JoinCodeExistsAsync(guarantorId, joinCode, ct));
        }
        else if (await _repository.JoinCodeExistsAsync(guarantorId, joinCode, ct))
        {
            throw new InvalidFundException("That join code is already in use for one of your funds.");
        }

        var fund = Fund.Create(
            guarantorId,
            request.Name,
            request.Type,
            request.CommittedCapital,
            joinCode,
            request.ContributionMultiplier,
            request.InterestRate);

        _repository.Add(fund);
        await _auditLog.RecordAsync(guarantorId, "Fund.Created", "Fund", fund.Id, null, ct);
        await _repository.SaveChangesAsync(ct);

        return fund;
    }

    public async Task<IReadOnlyList<Fund>> GetFundsForGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default)
    {
        return await _repository.GetByGuarantorAsync(guarantorId, ct);
    }

    /// <summary>
    /// Returns the fund only if it belongs to the supplied Guarantor.
    /// This enforces resource ownership server-side.
    /// </summary>
    public async Task<Fund?> GetFundForGuarantorAsync(
        Guid guarantorId,
        Guid fundId,
        CancellationToken ct = default)
    {
        var fund = await _repository.GetByIdAsync(fundId, ct);
        return fund is null || fund.GuarantorId != guarantorId ? null : fund;
    }

    public async Task<Fund?> UpdateFundAsync(
        Guid guarantorId,
        Guid fundId,
        Action<Fund> update,
        string auditAction,
        string? auditDetails = null,
        CancellationToken ct = default)
    {
        var fund = await GetFundForGuarantorAsync(guarantorId, fundId, ct);
        if (fund is null)
        {
            return null;
        }

        update(fund);
        await _auditLog.RecordAsync(guarantorId, auditAction, "Fund", fund.Id, auditDetails, ct);
        await _repository.SaveChangesAsync(ct);
        return fund;
    }
}
