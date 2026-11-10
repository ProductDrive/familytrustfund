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
    private readonly IFundGuarantorRepository _guarantorRepository;
    private readonly IFundAccessService _fundAccess;
    private readonly IAuditLog _auditLog;

    public FundService(
        IFundRepository repository,
        IFundGuarantorRepository guarantorRepository,
        IFundAccessService fundAccess,
        IAuditLog auditLog)
    {
        _repository = repository;
        _guarantorRepository = guarantorRepository;
        _fundAccess = fundAccess;
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
            // Join codes identify a fund globally, so generate until unique.
            do
            {
                joinCode = JoinCodeGenerator.Generate();
            } while (await _repository.JoinCodeExistsAsync(joinCode, ct));
        }
        else if (await _repository.JoinCodeExistsAsync(joinCode, ct))
        {
            throw new InvalidFundException("That join code is already in use.");
        }

        var fund = Fund.Create(
            guarantorId,
            request.Name,
            request.Type,
            request.CommittedCapital,
            joinCode,
            request.ContributionMultiplier,
            request.InterestRate,
            request.MinLoanTermMonths,
            request.MaxLoanTermMonths,
            request.GuarantorMode);

        _repository.Add(fund);

        // A Group Guarantor fund records the Fund Originator's own membership so
        // the group always has a complete Guarantor list (ADR-051). A
        // single-Guarantor fund creates no rows, preserving existing behaviour.
        if (fund.IsGroupFund)
        {
            _guarantorRepository.Add(
                FundGuarantor.CreateOriginator(fund.Id, guarantorId, request.CommittedCapital));
        }

        await _auditLog.RecordAsync(guarantorId, "Fund.Created", "Fund", fund.Id,
            $"GuarantorMode={fund.GuarantorMode}, MinLoanTermMonths={fund.MinLoanTermMonths}, MaxLoanTermMonths={fund.MaxLoanTermMonths}",
            ct);
        await _repository.SaveChangesAsync(ct);

        if (fund.IsGroupFund)
        {
            await _guarantorRepository.SaveChangesAsync(ct);
        }

        return fund;
    }

    public async Task<IReadOnlyList<Fund>> GetFundsForGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default)
    {
        // Originator funds plus funds where the caller is an active co-guarantor.
        return await _repository.GetByGuarantorAsync(guarantorId, ct);
    }

    /// <summary>
    /// Returns the fund only if the supplied Guarantor may operate it (originator
    /// or active co-guarantor). This enforces resource ownership server-side.
    /// </summary>
    public async Task<Fund?> GetFundForGuarantorAsync(
        Guid guarantorId,
        Guid fundId,
        CancellationToken ct = default)
    {
        var fund = await _repository.GetByIdAsync(fundId, ct);
        if (fund is null)
        {
            return null;
        }

        return await _fundAccess.CanAccessFundAsync(fund, guarantorId, ct) ? fund : null;
    }

    public async Task<Fund?> GetFundByIdAsync(Guid fundId, CancellationToken ct = default)
    {
        return await _repository.GetByIdAsync(fundId, ct);
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

    public async Task<Fund?> UpdateRepaymentAccountAsync(
        Guid guarantorId,
        Guid fundId,
        UpdateRepaymentAccountRequest request,
        CancellationToken ct = default)
    {
        var fund = await GetFundForGuarantorAsync(guarantorId, fundId, ct);
        if (fund is null)
        {
            return null;
        }

        fund.SetRepaymentAccount(request.BankName, request.AccountNumber, request.AccountName);
        await _auditLog.RecordAsync(guarantorId, "Fund.RepaymentAccountUpdated", "Fund", fund.Id, null, ct);
        await _repository.SaveChangesAsync(ct);
        return fund;
    }
}

public sealed record UpdateRepaymentAccountRequest(string? BankName, string? AccountNumber, string? AccountName);
