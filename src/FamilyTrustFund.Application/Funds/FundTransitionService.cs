using FamilyTrustFund.Application.Audit;
using FamilyTrustFund.Application.Contributions;
using FamilyTrustFund.Domain.Funds;

namespace FamilyTrustFund.Application.Funds;

/// <summary>
/// Computes Family-fund transition eligibility and performs the manual,
/// one-way transition to Family Capital (ADR-008). Eligibility is always
/// re-derived server-side from confirmed Family Contributions; a client cannot
/// force a transition the business rules do not allow.
/// </summary>
public class FundTransitionService
{
    private readonly IFundRepository _fundRepository;
    private readonly IContributionRepository _contributionRepository;
    private readonly IAuditLog _auditLog;

    public FundTransitionService(
        IFundRepository fundRepository,
        IContributionRepository contributionRepository,
        IAuditLog auditLog)
    {
        _fundRepository = fundRepository;
        _contributionRepository = contributionRepository;
        _auditLog = auditLog;
    }

    /// <summary>
    /// Returns the current transition state for a fund owned by the Guarantor,
    /// or <see langword="null"/> when the fund does not exist or is not theirs.
    /// </summary>
    public async Task<FundTransitionStatusDto?> GetTransitionStatusAsync(
        Guid guarantorId,
        Guid fundId,
        CancellationToken ct = default)
    {
        var fund = await _fundRepository.GetByIdAsync(fundId, ct);
        if (fund is null || fund.GuarantorId != guarantorId)
        {
            return null;
        }

        var totalContributions = fund.Type == FundType.Family
            ? await _contributionRepository.SumConfirmedByFundAsync(fund.Id, ct)
            : 0m;

        var isFamilyFund = fund.Type == FundType.Family;
        var isTransitioned = fund.Status == FundStatus.Transitioned;
        var isEligible = isFamilyFund
            && fund.Status == FundStatus.Active
            && !isTransitioned
            && totalContributions >= fund.CommittedCapital;

        return new FundTransitionStatusDto
        {
            FundId = fund.Id,
            IsFamilyFund = isFamilyFund,
            IsTransitioned = isTransitioned,
            CommittedCapital = fund.CommittedCapital,
            TotalFamilyContributions = totalContributions,
            IsEligible = isEligible,
            RemainingToThreshold = Math.Max(0m, fund.CommittedCapital - totalContributions),
        };
    }

    /// <summary>
    /// Performs the one-way transition for a Family fund owned by the Guarantor,
    /// or returns <see langword="null"/> when the fund does not exist or is not
    /// theirs. Throws when the transition is not currently permitted.
    /// </summary>
    public async Task<FundTransitionResultDto?> TransitionToFamilyCapitalAsync(
        Guid guarantorId,
        Guid fundId,
        CancellationToken ct = default)
    {
        var fund = await _fundRepository.GetByIdAsync(fundId, ct);
        if (fund is null || fund.GuarantorId != guarantorId)
        {
            return null;
        }

        if (fund.Type != FundType.Family)
        {
            throw new InvalidFundException("Only Family funds can transition to Family Capital.");
        }

        if (fund.Status == FundStatus.Transitioned)
        {
            throw new InvalidFundException("This fund has already transitioned to Family Capital.");
        }

        if (fund.Status == FundStatus.Inactive)
        {
            throw new InvalidFundException("Only active funds can transition to Family Capital.");
        }

        var totalContributions = await _contributionRepository.SumConfirmedByFundAsync(fund.Id, ct);
        if (totalContributions < fund.CommittedCapital)
        {
            throw new InvalidFundException(
                "This fund is not eligible yet. Family Contributions must reach or exceed your committed capital.");
        }

        fund.MarkTransitioned();

        await _auditLog.RecordAsync(guarantorId, "Fund.TransitionedToFamilyCapital", "Fund", fund.Id,
            $"FamilyContributions={totalContributions:N2}, CommittedCapital={fund.CommittedCapital:N2}", ct);
        await _fundRepository.SaveChangesAsync(ct);

        return new FundTransitionResultDto
        {
            FundId = fund.Id,
            CommittedCapital = fund.CommittedCapital,
            TotalFamilyContributions = totalContributions,
            TransitionedAtUtc = fund.TransitionedAtUtc ?? DateTime.UtcNow,
        };
    }
}