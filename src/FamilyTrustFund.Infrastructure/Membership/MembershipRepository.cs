using FamilyTrustFund.Application.Membership;
using FamilyTrustFund.Domain.Membership;
using FamilyTrustFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyTrustFund.Infrastructure.Membership;

public class MembershipRepository : IMembershipRepository
{
    private readonly ApplicationDbContext _db;

    public MembershipRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<FundMember?> GetByFundAndMemberAsync(Guid fundId, Guid memberId, CancellationToken ct = default) =>
        _db.FundMembers.FirstOrDefaultAsync(m => m.FundId == fundId && m.MemberId == memberId, ct);

    public Task<List<FundMember>> GetByMemberAsync(Guid memberId, CancellationToken ct = default) =>
        _db.FundMembers
            .Where(m => m.MemberId == memberId)
            .OrderBy(m => m.JoinedAtUtc)
            .ToListAsync(ct);

    public Task<List<FundMember>> GetByFundAsync(Guid fundId, CancellationToken ct = default) =>
        _db.FundMembers
            .Where(m => m.FundId == fundId)
            .OrderBy(m => m.JoinedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FundMemberItem>> GetMembersWithUserAsync(Guid fundId, CancellationToken ct = default) =>
        await _db.FundMembers
            .Where(m => m.FundId == fundId)
            .Join(_db.Users, m => m.MemberId, u => u.Id, (m, u) => new FundMemberItem
            {
                MemberId = m.MemberId,
                DisplayName = u.DisplayName,
                Email = u.Email ?? string.Empty,
                Status = m.Status,
                JoinedAtUtc = m.JoinedAtUtc,
            })
            .OrderBy(m => m.DisplayName)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AdminMembershipItem>> GetAllWithUserAndFundAsync(CancellationToken ct = default) =>
        await _db.FundMembers
            .Join(_db.Users, m => m.MemberId, u => u.Id, (m, u) => new { Membership = m, User = u })
            .Join(_db.Funds, x => x.Membership.FundId, f => f.Id, (x, f) => new AdminMembershipItem
            {
                MembershipId = x.Membership.Id,
                FundId = f.Id,
                FundName = f.Name,
                MemberId = x.User.Id,
                DisplayName = x.User.DisplayName,
                Email = x.User.Email ?? string.Empty,
                Status = x.Membership.Status,
                JoinedAtUtc = x.Membership.JoinedAtUtc,
            })
            .OrderByDescending(m => m.JoinedAtUtc)
            .ToListAsync(ct);

    public void Add(FundMember fundMember) => _db.FundMembers.Add(fundMember);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}