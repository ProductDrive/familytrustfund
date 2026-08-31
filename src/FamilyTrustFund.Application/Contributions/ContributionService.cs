using FamilyTrustFund.Application.Audit;
using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Application.Membership;
using FamilyTrustFund.Domain.Contributions;
using FamilyTrustFund.Domain.Financial;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Membership;

namespace FamilyTrustFund.Application.Contributions;

/// <summary>
/// Application service for Family fund contributions. Contribution eligibility,
/// Fund Credit and the confirmation workflow are all enforced server-side.
/// </summary>
public class ContributionService
{
    private readonly IContributionRepository _contributionRepository;
    private readonly IFundRepository _fundRepository;
    private readonly IMembershipRepository _membershipRepository;
    private readonly IAuditLog _auditLog;

    public ContributionService(
        IContributionRepository contributionRepository,
        IFundRepository fundRepository,
        IMembershipRepository membershipRepository,
        IAuditLog auditLog)
    {
        _contributionRepository = contributionRepository;
        _fundRepository = fundRepository;
        _membershipRepository = membershipRepository;
        _auditLog = auditLog;
    }

    /// <summary>
    /// Member reports a contribution to a Family fund. The server validates:
    /// <list type="bullet">
    ///   <item>The fund exists, is active and is a Family fund.</item>
    ///   <item>The caller is an active member of the fund.</item>
    ///   <item>The member does not have an active disbursed loan in the fund (ADR-006).</item>
    /// </list>
    /// The contribution is created as PendingConfirmation and does not affect
    /// Fund Credit until the Guarantor confirms it.
    /// </summary>
    public async Task<ContributionDto> ReportContributionAsync(
        Guid memberId,
        ReportContributionRequest request,
        CancellationToken ct = default)
    {
        var fund = await _fundRepository.GetByIdAsync(request.FundId, ct)
            ?? throw new InvalidContributionException("Fund not found.");

        if (fund.Type != FundType.Family)
        {
            throw new InvalidContributionException("Only Family funds accept contributions.");
        }

        if (fund.Status != FundStatus.Active)
        {
            throw new InvalidContributionException("That fund is not currently active.");
        }

        var membership = await _membershipRepository.GetByFundAndMemberAsync(fund.Id, memberId, ct);
        if (membership is null || membership.Status != MemberStatus.Active)
        {
            throw new InvalidContributionException("You are not an active member of that fund.");
        }

        if (await _contributionRepository.HasActiveDisbursedLoanAsync(memberId, fund.Id, ct))
        {
            throw new InvalidContributionException(
                "You cannot contribute while you have an active loan in this fund. Complete the loan first.");
        }

        var contribution = FundContribution.Report(
            fund.Id,
            memberId,
            request.Amount,
            request.Reference,
            request.Note);

        _contributionRepository.Add(contribution);
        await _auditLog.RecordAsync(memberId, "Contribution.Reported", "Contribution", contribution.Id,
            $"Amount={request.Amount}, Fund={fund.Name}", ct);
        await _contributionRepository.SaveChangesAsync(ct);

        return ToDto(contribution, fund, memberDisplayName: string.Empty, memberEmail: string.Empty);
    }

    /// <summary>
    /// Returns the member's Fund Credit summary across their funds.
    /// </summary>
    public async Task<IReadOnlyList<ContributionSummaryDto>> GetMyFundCreditAsync(
        Guid memberId,
        CancellationToken ct = default)
    {
        var memberships = await _membershipRepository.GetByMemberAsync(memberId, ct);
        var funds = await _fundRepository.GetByIdsAsync(memberships.Select(m => m.FundId).ToList(), ct);
        var fundById = funds.ToDictionary(f => f.Id);

        var results = new List<ContributionSummaryDto>();
        foreach (var membership in memberships)
        {
            if (!fundById.TryGetValue(membership.FundId, out var fund))
            {
                continue;
            }

            var fundCredit = await _contributionRepository.GetConfirmedFundCreditAsync(memberId, fund.Id, ct);
            var hasActiveLoan = await _contributionRepository.HasActiveDisbursedLoanAsync(memberId, fund.Id, ct);

            results.Add(new ContributionSummaryDto
            {
                FundId = fund.Id,
                MemberId = memberId,
                FundCredit = fundCredit,
                BorrowingEntitlement = fund.Type == FundType.Family
                    ? FundFinancialRules.FamilyBorrowingEntitlement(fundCredit, fund.ContributionMultiplier)
                    : null,
                HasActiveDisbursedLoan = hasActiveLoan,
            });
        }

        return results;
    }

    /// <summary>
    /// Returns all contributions made by a member across their funds.
    /// </summary>
    public async Task<IReadOnlyList<ContributionDto>> GetMyContributionsAsync(
        Guid memberId,
        CancellationToken ct = default)
    {
        var items = await _contributionRepository.GetByMemberAsync(memberId, ct);
        return items.Select(i => ToDto(i.Contribution, i.FundName, i.MemberDisplayName, i.MemberEmail)).ToList();
    }

    /// <summary>
    /// Returns pending contributions for the Guarantor's confirmation queue.
    /// </summary>
    public async Task<IReadOnlyList<ContributionDto>> GetPendingForGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default)
    {
        var items = await _contributionRepository.GetPendingForGuarantorAsync(guarantorId, ct);
        return items.Select(i => ToDto(i.Contribution, i.FundName, i.MemberDisplayName, i.MemberEmail)).ToList();
    }

    /// <summary>
    /// Returns all contributions for a fund. Ownership is enforced by the caller.
    /// </summary>
    public async Task<IReadOnlyList<ContributionDto>> GetByFundAsync(
        Guid fundId,
        CancellationToken ct = default)
    {
        var items = await _contributionRepository.GetByFundAsync(fundId, ct);
        return items.Select(i => ToDto(i.Contribution, i.FundName, i.MemberDisplayName, i.MemberEmail)).ToList();
    }

    /// <summary>
    /// Guarantor confirms a pending contribution, posting it to Fund Credit.
    /// </summary>
    public async Task<ContributionDto> ConfirmContributionAsync(
        Guid guarantorId,
        ConfirmContributionRequest request,
        CancellationToken ct = default)
    {
        var contribution = await _contributionRepository.GetByIdAsync(request.ContributionId, ct)
            ?? throw new InvalidContributionException("Contribution not found.");

        var fund = await _fundRepository.GetByIdAsync(contribution.FundId, ct)
            ?? throw new InvalidContributionException("Fund not found.");

        if (fund.GuarantorId != guarantorId)
        {
            throw new InvalidContributionException("You do not have permission to confirm this contribution.");
        }

        contribution.Confirm();

        await _auditLog.RecordAsync(guarantorId, "Contribution.Confirmed", "Contribution", contribution.Id,
            $"Amount={contribution.Amount}", ct);
        await _contributionRepository.SaveChangesAsync(ct);

        return ToDto(contribution, fund, string.Empty, string.Empty);
    }

    /// <summary>
    /// Guarantor rejects a pending contribution. A rejected contribution never
    /// affects Fund Credit.
    /// </summary>
    public async Task<ContributionDto> RejectContributionAsync(
        Guid guarantorId,
        RejectContributionRequest request,
        CancellationToken ct = default)
    {
        var contribution = await _contributionRepository.GetByIdAsync(request.ContributionId, ct)
            ?? throw new InvalidContributionException("Contribution not found.");

        var fund = await _fundRepository.GetByIdAsync(contribution.FundId, ct)
            ?? throw new InvalidContributionException("Fund not found.");

        if (fund.GuarantorId != guarantorId)
        {
            throw new InvalidContributionException("You do not have permission to reject this contribution.");
        }

        contribution.Reject(request.Reason);

        await _auditLog.RecordAsync(guarantorId, "Contribution.Rejected", "Contribution", contribution.Id,
            request.Reason is not null ? $"Reason={request.Reason}" : null, ct);
        await _contributionRepository.SaveChangesAsync(ct);

        return ToDto(contribution, fund, string.Empty, string.Empty);
    }

    private static ContributionDto ToDto(
        FundContribution contribution,
        Fund fund,
        string memberDisplayName,
        string memberEmail) => new()
    {
        Id = contribution.Id,
        FundId = contribution.FundId,
        FundName = fund.Name,
        MemberId = contribution.MemberId,
        MemberDisplayName = memberDisplayName,
        MemberEmail = memberEmail,
        Amount = contribution.Amount,
        Status = contribution.Status,
        Reference = contribution.Reference,
        Note = contribution.Note,
        RejectionReason = contribution.RejectionReason,
        ReportedAtUtc = contribution.ReportedAtUtc,
        ConfirmedAtUtc = contribution.ConfirmedAtUtc,
        RejectedAtUtc = contribution.RejectedAtUtc,
    };

    private static ContributionDto ToDto(
        FundContribution contribution,
        string fundName,
        string memberDisplayName,
        string memberEmail) => new()
    {
        Id = contribution.Id,
        FundId = contribution.FundId,
        FundName = fundName,
        MemberId = contribution.MemberId,
        MemberDisplayName = memberDisplayName,
        MemberEmail = memberEmail,
        Amount = contribution.Amount,
        Status = contribution.Status,
        Reference = contribution.Reference,
        Note = contribution.Note,
        RejectionReason = contribution.RejectionReason,
        ReportedAtUtc = contribution.ReportedAtUtc,
        ConfirmedAtUtc = contribution.ConfirmedAtUtc,
        RejectedAtUtc = contribution.RejectedAtUtc,
    };
}
