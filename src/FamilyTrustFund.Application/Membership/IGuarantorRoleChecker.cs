namespace FamilyTrustFund.Application.Membership;

public interface IGuarantorRoleChecker
{
    Task<bool> IsGuarantorAsync(Guid userId, CancellationToken ct = default);
}
