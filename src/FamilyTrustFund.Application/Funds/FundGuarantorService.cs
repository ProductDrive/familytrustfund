using FamilyTrustFund.Application.Audit;
using FamilyTrustFund.Application.Loans;
using FamilyTrustFund.Domain.Funds;

namespace FamilyTrustFund.Application.Funds;

/// <summary>
/// Application service for Group Guarantor membership: invitations, acceptance,
/// removal, leaving and per-Guarantor committed capital (ADR-051).
/// </summary>
/// <remarks>
/// Single-Guarantor funds never use this service: they have no
/// <see cref="FundGuarantor"/> rows and keep their existing behaviour. Every
/// operation re-checks authorization and the fund's live state server-side.
/// </remarks>
public class FundGuarantorService
{
    /// <summary>A group fund must be operated by at least this many Guarantors.</summary>
    public const int MinActiveGuarantors = 2;

    /// <summary>Upper bound on active Guarantors in one group fund.</summary>
    public const int MaxActiveGuarantors = 10;

    private readonly IFundRepository _fundRepository;
    private readonly IFundGuarantorRepository _guarantorRepository;
    private readonly IFundAccessService _fundAccess;
    private readonly ILoanRepository _loanRepository;
    private readonly IAuditLog _auditLog;
    private readonly GuarantorNotificationService _notifications;

    public FundGuarantorService(
        IFundRepository fundRepository,
        IFundGuarantorRepository guarantorRepository,
        IFundAccessService fundAccess,
        ILoanRepository loanRepository,
        IAuditLog auditLog,
        GuarantorNotificationService notifications)
    {
        _fundRepository = fundRepository;
        _guarantorRepository = guarantorRepository;
        _fundAccess = fundAccess;
        _loanRepository = loanRepository;
        _auditLog = auditLog;
        _notifications = notifications;
    }

    /// <summary>Lists the Guarantors of a fund (originator plus invited/active).</summary>
    public async Task<IReadOnlyList<FundGuarantorDto>?> GetFundGuarantorsAsync(
        Guid userId,
        Guid fundId,
        CancellationToken ct = default)
    {
        var fund = await _fundRepository.GetByIdAsync(fundId, ct);
        if (fund is null || !await _fundAccess.CanAccessFundAsync(fund, userId, ct))
        {
            return null;
        }

        var memberships = await _guarantorRepository.GetByFundAsync(fundId, ct);
        var profiles = await _guarantorRepository.GetProfilesAsync(
            memberships.Select(m => m.GuarantorId),
            ct);

        return memberships.Select(m => ToDto(m, profiles)).ToList();
    }

    /// <summary>
    /// The Fund Originator invites an existing, approved Guarantor account to
    /// operate the fund.
    /// </summary>
    public async Task<FundGuarantorDto> InviteAsync(
        Guid originatorId,
        Guid fundId,
        InviteGuarantorRequest request,
        CancellationToken ct = default)
    {
        var fund = await GetGroupFundForOriginatorAsync(originatorId, fundId, ct);
        await EnsureNoPendingLoanAsync(fund, ct);

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new InvalidFundGuarantorException("The invited Guarantor's email is required.");
        }

        var account = await _guarantorRepository.FindGuarantorByEmailAsync(request.Email, ct);
        if (account is null || !account.IsApprovedGuarantor)
        {
            throw new InvalidFundGuarantorException(
                "Only an existing, approved Guarantor account can be invited. Ask them to register and be approved first.");
        }

        if (account.Id == originatorId)
        {
            throw new InvalidFundGuarantorException("You cannot invite yourself.");
        }

        var activeCount = await _guarantorRepository.CountActiveAsync(fundId, ct);
        if (activeCount >= MaxActiveGuarantors)
        {
            throw new InvalidFundGuarantorException(
                $"A group fund can have at most {MaxActiveGuarantors} active Guarantors.");
        }

        var existing = await _guarantorRepository.GetAsync(fundId, account.Id, ct);
        FundGuarantor membership;

        if (existing is null)
        {
            membership = FundGuarantor.Invite(
                fundId,
                account.Id,
                account.Email,
                request.CommittedCapital,
                originatorId);
            _guarantorRepository.Add(membership);
        }
        else if (existing.Status == FundGuarantorStatus.Active)
        {
            throw new InvalidFundGuarantorException("That Guarantor is already an active member of this fund.");
        }
        else if (existing.Status == FundGuarantorStatus.Invited && existing.IsOpenInvitation)
        {
            throw new InvalidFundGuarantorException("That Guarantor already has an open invitation to this fund.");
        }
        else
        {
            existing.ReinstateInvitation(request.CommittedCapital, originatorId);
            membership = existing;
        }

        await _auditLog.RecordAsync(originatorId, "FundGuarantor.Invited", "Fund", fundId,
            $"GuarantorId={account.Id}, CommittedCapital={request.CommittedCapital:N2}", ct);
        await _guarantorRepository.SaveChangesAsync(ct);

        await _notifications.InvitationReceivedAsync(
            fund, account.Id, request.CommittedCapital, ct);

        var profiles = await _guarantorRepository.GetProfilesAsync(new[] { account.Id }, ct);
        return ToDto(membership, profiles);
    }

    /// <summary>
    /// The Fund Originator withdraws an invitation or removes an active
    /// co-guarantor. The originator can never remove themselves.
    /// </summary>
    public async Task<FundGuarantorDto> RevokeAsync(
        Guid originatorId,
        Guid fundId,
        Guid guarantorId,
        CancellationToken ct = default)
    {
        var fund = await GetGroupFundForOriginatorAsync(originatorId, fundId, ct);

        if (guarantorId == originatorId)
        {
            throw new InvalidFundGuarantorException("The Fund Originator cannot be removed from the fund.");
        }

        var membership = await _guarantorRepository.GetAsync(fundId, guarantorId, ct)
            ?? throw new InvalidFundGuarantorException("That Guarantor is not part of this fund.");

        await EnsureNoPendingLoanAsync(fund, ct);

        if (membership.IsActive)
        {
            var activeDisbursed = await _loanRepository.SumActiveDisbursedByFundAsync(fundId, ct);
            fund.DecreaseCommittedCapital(membership.CommittedCapital, activeDisbursed);
        }

        membership.Revoke();

        await _auditLog.RecordAsync(originatorId, "FundGuarantor.Removed", "Fund", fundId,
            $"GuarantorId={guarantorId}", ct);
        await _guarantorRepository.SaveChangesAsync(ct);
        await _fundRepository.SaveChangesAsync(ct);

        return ToDto(membership, await _guarantorRepository.GetProfilesAsync(new[] { guarantorId }, ct));
    }

    /// <summary>An active co-guarantor leaves the group voluntarily.</summary>
    public async Task LeaveAsync(Guid guarantorId, Guid fundId, CancellationToken ct = default)
    {
        var fund = await _fundRepository.GetByIdAsync(fundId, ct)
            ?? throw new InvalidFundGuarantorException("Fund not found.");

        if (!fund.IsGroupFund)
        {
            throw new InvalidFundGuarantorException("This is not a group fund.");
        }

        if (fund.GuarantorId == guarantorId)
        {
            throw new InvalidFundGuarantorException(
                "The Fund Originator cannot leave. Create another fund profile if you need a separate group.");
        }

        var membership = await _guarantorRepository.GetAsync(fundId, guarantorId, ct);
        if (membership is null || !membership.IsActive)
        {
            throw new InvalidFundGuarantorException("You are not an active Guarantor of this fund.");
        }

        await EnsureNoPendingLoanAsync(fund, ct);

        var activeDisbursed = await _loanRepository.SumActiveDisbursedByFundAsync(fundId, ct);
        fund.DecreaseCommittedCapital(membership.CommittedCapital, activeDisbursed);
        membership.Leave();

        await _auditLog.RecordAsync(guarantorId, "FundGuarantor.Left", "Fund", fundId, null, ct);
        await _guarantorRepository.SaveChangesAsync(ct);
        await _fundRepository.SaveChangesAsync(ct);

        await _notifications.GuarantorLeftAsync(
            fund,
            fund.GuarantorId,
            (await _guarantorRepository.GetProfilesAsync(new[] { guarantorId }, ct))
                .GetValueOrDefault(guarantorId)?.DisplayName ?? "A Guarantor",
            ct);
    }

    /// <summary>The invited Guarantor accepts an open invitation.</summary>
    public async Task<GuarantorInvitationDto> AcceptAsync(
        Guid guarantorId,
        Guid membershipId,
        CancellationToken ct = default)
    {
        var membership = await GetInvitationAsync(guarantorId, membershipId, ct);
        var fund = await _fundRepository.GetByIdAsync(membership.FundId, ct)
            ?? throw new InvalidFundGuarantorException("Fund not found.");

        if (fund.Status != FundStatus.Active)
        {
            throw new InvalidFundGuarantorException("That fund is not currently active.");
        }

        await EnsureNoPendingLoanAsync(fund, ct);

        var activeCount = await _guarantorRepository.CountActiveAsync(fund.Id, ct);
        if (activeCount >= MaxActiveGuarantors)
        {
            throw new InvalidFundGuarantorException(
                $"A group fund can have at most {MaxActiveGuarantors} active Guarantors.");
        }

        membership.Accept();
        fund.IncreaseCommittedCapital(membership.CommittedCapital);

        await _auditLog.RecordAsync(guarantorId, "FundGuarantor.InvitationAccepted", "Fund", fund.Id,
            $"CommittedCapital={membership.CommittedCapital:N2}", ct);
        await _guarantorRepository.SaveChangesAsync(ct);
        await _fundRepository.SaveChangesAsync(ct);

        var profiles = await _guarantorRepository.GetProfilesAsync(new[] { guarantorId }, ct);
        await _notifications.InvitationAcceptedAsync(
            fund,
            fund.GuarantorId,
            profiles.GetValueOrDefault(guarantorId)?.DisplayName ?? "A Guarantor",
            ct);

        return await ToInvitationDtoAsync(membership, ct);
    }

    /// <summary>The invited Guarantor declines an open invitation.</summary>
    public async Task<GuarantorInvitationDto> DeclineAsync(
        Guid guarantorId,
        Guid membershipId,
        CancellationToken ct = default)
    {
        var membership = await GetInvitationAsync(guarantorId, membershipId, ct);
        membership.Decline();

        await _auditLog.RecordAsync(guarantorId, "FundGuarantor.InvitationDeclined", "Fund", membership.FundId,
            null, ct);
        await _guarantorRepository.SaveChangesAsync(ct);

        var fund = await _fundRepository.GetByIdAsync(membership.FundId, ct);
        if (fund is not null)
        {
            var profiles = await _guarantorRepository.GetProfilesAsync(new[] { guarantorId }, ct);
            await _notifications.InvitationDeclinedAsync(
                fund,
                fund.GuarantorId,
                profiles.GetValueOrDefault(guarantorId)?.DisplayName ?? "A Guarantor",
                ct);
        }

        return await ToInvitationDtoAsync(membership, ct);
    }

    /// <summary>Changes the caller's own committed capital for a group fund.</summary>
    public async Task<FundGuarantorDto> UpdateCommitmentAsync(
        Guid guarantorId,
        Guid fundId,
        UpdateGuarantorCommitmentRequest request,
        CancellationToken ct = default)
    {
        var fund = await _fundRepository.GetByIdAsync(fundId, ct)
            ?? throw new InvalidFundGuarantorException("Fund not found.");

        if (!fund.IsGroupFund)
        {
            throw new InvalidFundGuarantorException(
                "Committed capital can only be changed per Guarantor in a group fund.");
        }

        var membership = await _guarantorRepository.GetAsync(fundId, guarantorId, ct);
        if (membership is null || !membership.IsActive)
        {
            throw new InvalidFundGuarantorException("You are not an active Guarantor of this fund.");
        }

        await EnsureNoPendingLoanAsync(fund, ct);

        var delta = request.CommittedCapital - membership.CommittedCapital;
        if (delta > 0)
        {
            fund.IncreaseCommittedCapital(delta);
        }
        else if (delta < 0)
        {
            var activeDisbursed = await _loanRepository.SumActiveDisbursedByFundAsync(fundId, ct);
            fund.DecreaseCommittedCapital(-delta, activeDisbursed);
        }

        membership.SetCommittedCapital(request.CommittedCapital);

        await _auditLog.RecordAsync(guarantorId, "FundGuarantor.CommitmentChanged", "Fund", fundId,
            $"CommittedCapital={request.CommittedCapital:N2}", ct);
        await _guarantorRepository.SaveChangesAsync(ct);
        await _fundRepository.SaveChangesAsync(ct);

        return ToDto(membership, await _guarantorRepository.GetProfilesAsync(new[] { guarantorId }, ct));
    }

    /// <summary>All group memberships and invitations for the caller.</summary>
    public async Task<IReadOnlyList<GuarantorInvitationDto>> GetMineAsync(
        Guid guarantorId,
        CancellationToken ct = default)
    {
        var items = await _guarantorRepository.GetByGuarantorAsync(guarantorId, ct);
        return items.Select(i => ToInvitationDto(i)).ToList();
    }

    private async Task<Fund> GetGroupFundForOriginatorAsync(
        Guid originatorId,
        Guid fundId,
        CancellationToken ct)
    {
        var fund = await _fundRepository.GetByIdAsync(fundId, ct)
            ?? throw new InvalidFundGuarantorException("Fund not found.");

        if (fund.GuarantorId != originatorId)
        {
            throw new InvalidFundGuarantorException("Only the Fund Originator can manage Group Guarantors.");
        }

        if (!fund.IsGroupFund)
        {
            throw new InvalidFundGuarantorException(
                "This is a single-Guarantor fund. Create a group fund to invite Guarantors.");
        }

        if (fund.Status != FundStatus.Active)
        {
            throw new InvalidFundGuarantorException("That fund is not currently active.");
        }

        return fund;
    }

    private async Task<FundGuarantor> GetInvitationAsync(
        Guid guarantorId,
        Guid membershipId,
        CancellationToken ct)
    {
        var membership = await _guarantorRepository.GetByIdAsync(membershipId, ct)
            ?? throw new InvalidFundGuarantorException("Invitation not found.");

        if (membership.GuarantorId != guarantorId)
        {
            throw new InvalidFundGuarantorException("This invitation is not yours.");
        }

        if (membership.Status != FundGuarantorStatus.Invited)
        {
            throw new InvalidFundGuarantorException("Only a pending invitation can be answered.");
        }

        if (membership.IsExpired)
        {
            membership.MarkExpired();
            await _guarantorRepository.SaveChangesAsync(ct);
            throw new InvalidFundGuarantorException("This invitation has expired.");
        }

        return membership;
    }

    /// <summary>
    /// Group membership changes are blocked while any loan in the fund is still
    /// awaiting decisions, so the approver set cannot shift mid-vote (ADR-051).
    /// </summary>
    private async Task EnsureNoPendingLoanAsync(Fund fund, CancellationToken ct)
    {
        if (await _loanRepository.HasPendingInFundAsync(fund.Id, ct))
        {
            throw new InvalidFundGuarantorException(
                "Group Guarantors cannot change while a loan request is awaiting decisions.");
        }
    }

    private async Task<GuarantorInvitationDto> ToInvitationDtoAsync(
        FundGuarantor membership,
        CancellationToken ct)
    {
        var details = (await _guarantorRepository.GetByGuarantorAsync(membership.GuarantorId, ct))
            .FirstOrDefault(i => i.Membership.Id == membership.Id);

        return details is null
            ? new GuarantorInvitationDto
            {
                MembershipId = membership.Id,
                FundId = membership.FundId,
                CommittedCapital = membership.CommittedCapital,
                Status = membership.Status,
                InvitedAtUtc = membership.InvitedAtUtc,
                ExpiresAtUtc = membership.ExpiresAtUtc,
                IsExpired = membership.IsExpired,
            }
            : ToInvitationDto(details);
    }

    private static GuarantorInvitationDto ToInvitationDto(FundGuarantorWithDetails details) => new()
    {
        MembershipId = details.Membership.Id,
        FundId = details.FundId,
        FundName = details.FundName,
        FundType = details.FundType,
        OriginatorId = details.OriginatorId,
        OriginatorDisplayName = details.OriginatorDisplayName,
        OriginatorEmail = details.OriginatorEmail,
        CommittedCapital = details.Membership.CommittedCapital,
        Status = details.Membership.Status,
        InvitedAtUtc = details.Membership.InvitedAtUtc,
        ExpiresAtUtc = details.Membership.ExpiresAtUtc,
        IsExpired = details.Membership.IsExpired,
    };

    private static FundGuarantorDto ToDto(
        FundGuarantor membership,
        IReadOnlyDictionary<Guid, GuarantorProfile> profiles)
    {
        var profile = profiles.GetValueOrDefault(membership.GuarantorId);
        return new FundGuarantorDto
        {
            Id = membership.Id,
            FundId = membership.FundId,
            GuarantorId = membership.GuarantorId,
            GuarantorDisplayName = profile?.DisplayName ?? string.Empty,
            GuarantorEmail = profile?.Email ?? membership.InvitedEmail,
            Role = membership.Role,
            Status = membership.Status,
            CommittedCapital = membership.CommittedCapital,
            InvitedAtUtc = membership.InvitedAtUtc,
            RespondedAtUtc = membership.RespondedAtUtc,
            ExpiresAtUtc = membership.ExpiresAtUtc,
            IsExpired = membership.IsExpired,
            IsOriginator = membership.IsOriginator,
        };
    }
}
