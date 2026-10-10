using FamilyTrustFund.Application.Auth;
using FamilyTrustFund.Domain.Auth;
using FamilyTrustFund.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyTrustFund.Infrastructure.Auth;

public class LoginOtpRepository : ILoginOtpRepository
{
    private readonly ApplicationDbContext _db;

    public LoginOtpRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<LoginOtp?> GetLatestByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        _db.LoginOtps
            .Where(o => o.Email == normalizedEmail)
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    public Task<LoginOtp?> GetLatestUnconsumedAsync(string normalizedEmail, CancellationToken ct = default) =>
        _db.LoginOtps
            .Where(o => o.Email == normalizedEmail && o.ConsumedAtUtc == null)
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<LoginOtp>> GetUnconsumedByEmailAsync(
        string normalizedEmail,
        CancellationToken ct = default) =>
        await _db.LoginOtps
            .Where(o => o.Email == normalizedEmail && o.ConsumedAtUtc == null)
            .ToListAsync(ct);

    public void Add(LoginOtp otp) => _db.LoginOtps.Add(otp);

    public void RemoveRange(IEnumerable<LoginOtp> otps) => _db.LoginOtps.RemoveRange(otps);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}