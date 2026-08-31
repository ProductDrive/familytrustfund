using FamilyTrustFund.Application.Audit;
using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Membership;

namespace FamilyTrustFund.Application.Membership;

/// <summary>
/// Application service for fund membership. Join codes invite members into a
/// closed group; all membership decisions are validated server-side.
/// </summary>
public class MembershipService
{
    private readonly IFundRepository _fundRepository;
    private readonly IMembershipRepository _membershipRepository;
    private readonly IAuditLog _auditLog;

    public MembershipService(
        IFundRepository fundRepository,
        IMembershipRepository membershipRepository,
        IAuditLog auditLog)
    {
        _fundRepository = fundRepository;
        _membershipRepository = membershipRepository;
        _auditLog = auditLog;
    }

    /// <summary>
    /// Joins the authenticated user to the fund matching the join code.
    /// </summary>
    /// <exception cref="InvalidMembershipException">
    /// When the code is unknown, the fund is not active, the user owns the fund,
    /// or the user is already a member.
    /// </exception>
    public async Task<MembershipDto> JoinFundAsync(
        Guid memberId,
        JoinFundRequest request,
        CancellationToken ct = default)
    {
        var joinCode = request.JoinCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            throw new InvalidMembershipException("Join code is required.");
        }

        var fund = await _fundRepository.GetByJoinCodeAsync(joinCode, ct);
        if (fund is null)
        {
            throw new InvalidMembershipException("No fund found for that join code.");
        }

        if (fund.Status != FundStatus.Active)
        {
            throw new InvalidMembershipException("That fund is not accepting members right now.");
        }

        if (fund.GuarantorId == memberId)
        {
            throw new InvalidMembershipException("A Guarantor cannot join a fund they own.");
        }

        if (await _membershipRepository.GetByFundAndMemberAsync(fund.Id, memberId, ct) is not null)
        {
            throw new InvalidMembershipException("You are already a member of that fund.");
        }

        var membership = FundMember.Join(fund.Id, memberId);
        _membershipRepository.Add(membership);
        await _auditLog.RecordAsync(memberId, "Membership.Joined", "Fund", fund.Id, null, ct);
        await _membershipRepository.SaveChangesAsync(ct);

        return ToMembershipDto(membership, fund);
    }

    /// <summary>
    /// Returns every fund the member belongs to, for their dashboard.
    /// </summary>
    public async Task<IReadOnlyList<MembershipDto>> GetMyMembershipsAsync(
        Guid memberId,
        CancellationToken ct = default)
    {
        var memberships = await _membershipRepository.GetByMemberAsync(memberId, ct);
        if (memberships.Count == 0)
        {
            return Array.Empty<MembershipDto>();
        }

        var funds = await _fundRepository.GetByIdsAsync(
            memberships.Select(m => m.FundId).Distinct(), ct);
        var fundById = funds.ToDictionary(f => f.Id);

        return memberships
            .Where(m => fundById.ContainsKey(m.FundId))
            .Select(m => ToMembershipDto(m, fundById[m.FundId]))
            .ToList();
    }

    /// <summary>
    /// Returns the members of a fund. Returns <see langword="null"/> when the
    /// fund does not exist or does not belong to the supplied Guarantor,
    /// enforcing ownership server-side.
    /// </summary>
    public async Task<IReadOnlyList<FundMemberItem>?> GetFundMembersAsync(
        Guid guarantorId,
        Guid fundId,
        CancellationToken ct = default)
    {
        var fund = await _fundRepository.GetByIdAsync(fundId, ct);
        if (fund is null || fund.GuarantorId != guarantorId)
        {
            return null;
        }

        return await _membershipRepository.GetMembersWithUserAsync(fundId, ct);
    }

    /// <summary>
    /// Platform-wide membership view for Super Admin.
    /// </summary>
    public Task<IReadOnlyList<AdminMembershipItem>> GetAllMembershipsAsync(
        CancellationToken ct = default) =>
        _membershipRepository.GetAllWithUserAndFundAsync(ct);

    private static MembershipDto ToMembershipDto(FundMember member, Fund fund) => new()
    {
        FundId = fund.Id,
        FundName = fund.Name,
        FundType = fund.Type,
        FundStatus = fund.Status,
        JoinCode = fund.JoinCode,
        ContributionMultiplier = fund.ContributionMultiplier,
        InterestRate = fund.InterestRate,
        HowItWorks = fund.HowItWorks,
        Status = member.Status,
        JoinedAtUtc = member.JoinedAtUtc,
    };
}