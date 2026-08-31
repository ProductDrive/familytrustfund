using FamilyTrustFund.Domain.Membership;

namespace FamilyTrustFund.Application.Membership;

/// <summary>
/// Persistence port for fund memberships. Implemented by the infrastructure layer.
/// </summary>
public interface IMembershipRepository
{
    Task<FundMember?> GetByFundAndMemberAsync(Guid fundId, Guid memberId, CancellationToken ct = default);
    Task<List<FundMember>> GetByMemberAsync(Guid memberId, CancellationToken ct = default);
    Task<List<FundMember>> GetByFundAsync(Guid fundId, CancellationToken ct = default);
    Task<IReadOnlyList<FundMemberItem>> GetMembersWithUserAsync(Guid fundId, CancellationToken ct = default);
    Task<IReadOnlyList<AdminMembershipItem>> GetAllWithUserAndFundAsync(CancellationToken ct = default);
    void Add(FundMember fundMember);
    Task SaveChangesAsync(CancellationToken ct = default);
}