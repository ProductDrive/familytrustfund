using FamilyTrustFund.Application.Notifications;
using FamilyTrustFund.Domain.Funds;

namespace FamilyTrustFund.Application.Funds;

/// <summary>
/// Sends the Group Guarantor transactional emails using the existing
/// <see cref="IEmailSender"/> infrastructure — no notification outbox (D16).
/// </summary>
/// <remarks>
/// Sending happens after the financial/authorization transaction commits and
/// never throws: a mail failure must not roll back a committed decision.
/// </remarks>
public class GuarantorNotificationService
{
    private readonly IEmailSender _emailSender;
    private readonly IFundGuarantorRepository _guarantorRepository;

    public GuarantorNotificationService(
        IEmailSender emailSender,
        IFundGuarantorRepository guarantorRepository)
    {
        _emailSender = emailSender;
        _guarantorRepository = guarantorRepository;
    }

    public Task InvitationReceivedAsync(
        Fund fund,
        Guid invitedGuarantorId,
        decimal committedCapital,
        CancellationToken ct = default) =>
        NotifyAsync(
            invitedGuarantorId,
            $"{fund.Name}: you have been invited as a Group Guarantor",
            $"""
             You have been invited to operate the fund "{fund.Name}" as a Group Guarantor.

             Committed capital on your side: ₦{committedCapital:N2}
             This is a planning figure and is not money deposited into the platform.

             Sign in to Family Trust Fund and open Invitations to accept or decline.
             The invitation expires after {FundGuarantor.InvitationLifetimeDays} days.
             """,
            ct);

    public Task InvitationAcceptedAsync(
        Fund fund,
        Guid originatorId,
        string guarantorDisplayName,
        CancellationToken ct = default) =>
        NotifyAsync(
            originatorId,
            $"{fund.Name}: a Group Guarantor accepted your invitation",
            $"{guarantorDisplayName} accepted your invitation and now jointly operates \"{fund.Name}\".",
            ct);

    public Task InvitationDeclinedAsync(
        Fund fund,
        Guid originatorId,
        string guarantorDisplayName,
        CancellationToken ct = default) =>
        NotifyAsync(
            originatorId,
            $"{fund.Name}: a Group Guarantor declined your invitation",
            $"{guarantorDisplayName} declined your invitation to operate \"{fund.Name}\".",
            ct);

    public Task GuarantorLeftAsync(
        Fund fund,
        Guid originatorId,
        string guarantorDisplayName,
        CancellationToken ct = default) =>
        NotifyAsync(
            originatorId,
            $"{fund.Name}: a Group Guarantor left the fund",
            $"{guarantorDisplayName} is no longer a Group Guarantor of \"{fund.Name}\".",
            ct);

    public Task LoanAwaitingDecisionAsync(
        Fund fund,
        Guid approverId,
        decimal amount,
        CancellationToken ct = default) =>
        NotifyAsync(
            approverId,
            $"{fund.Name}: a loan request needs your approval",
            $"""
             A member requested a loan of ₦{amount:N2} from "{fund.Name}".

             Every active Group Guarantor must approve before the loan can be disbursed.
             Sign in to Family Trust Fund to review and decide.
             """,
            ct);

    public Task LoanApprovedAsync(
        Fund fund,
        Guid memberId,
        decimal amount,
        CancellationToken ct = default) =>
        NotifyAsync(
            memberId,
            $"{fund.Name}: your loan was approved",
            $"Your loan request for ₦{amount:N2} from \"{fund.Name}\" was approved.",
            ct);

    public Task LoanRejectedAsync(
        Fund fund,
        Guid memberId,
        string? reason,
        CancellationToken ct = default) =>
        NotifyAsync(
            memberId,
            $"{fund.Name}: your loan request was declined",
            reason is null
                ? $"Your loan request from \"{fund.Name}\" was declined."
                : $"Your loan request from \"{fund.Name}\" was declined. Reason: {reason}",
            ct);

    private async Task NotifyAsync(
        Guid userId,
        string subject,
        string body,
        CancellationToken ct)
    {
        try
        {
            var profiles = await _guarantorRepository.GetProfilesAsync(new[] { userId }, ct);
            if (!profiles.TryGetValue(userId, out var profile) || string.IsNullOrWhiteSpace(profile.Email))
            {
                return;
            }

            await _emailSender.SendAsync(
                new EmailMessage
                {
                    To = profile.Email,
                    DisplayName = profile.DisplayName,
                    Subject = subject,
                    Body = body,
                },
                ct);
        }
        catch (EmailDeliveryException)
        {
            // Notification failures must never change a committed financial or
            // authorization decision (AGENTS §10).
        }
    }
}
