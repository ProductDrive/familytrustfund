namespace FamilyTrustFund.Domain.Funds;

/// <summary>
/// A Guarantor's relationship to a Group Guarantor fund: either the Fund
/// Originator's membership, an outstanding invitation, or an active/ended
/// co-guarantor membership (ADR-051).
/// </summary>
/// <remarks>
/// Single-Guarantor funds never create rows here; <see cref="Fund.GuarantorId"/>
/// remains the sole owner. For group funds the originator has an
/// <see cref="FundGuarantorRole.Originator"/> row and every invited Guarantor
/// has a row whose <see cref="Status"/> tracks the invitation/membership
/// lifecycle. The unique index on <c>(FundId, GuarantorId)</c> prevents
/// duplicate invitations and double joins.
/// </remarks>
public class FundGuarantor
{
    /// <summary>How long an invitation stays open before it expires.</summary>
    public const int InvitationLifetimeDays = 14;

    public Guid Id { get; private set; }
    public Guid FundId { get; private set; }
    public Guid GuarantorId { get; private set; }

    public FundGuarantorRole Role { get; private set; }
    public FundGuarantorStatus Status { get; private set; }

    /// <summary>
    /// The Guarantor's committed capital for this fund. A planning figure only;
    /// it is never money deposited into the platform (ADR-002).
    /// </summary>
    public decimal CommittedCapital { get; private set; }

    /// <summary>Email address the invitation was sent to.</summary>
    public string InvitedEmail { get; private set; } = string.Empty;

    /// <summary>Guarantor who issued the invitation (the Fund Originator).</summary>
    public Guid InvitedByUserId { get; private set; }

    public DateTime InvitedAtUtc { get; private set; }
    public DateTime? RespondedAtUtc { get; private set; }

    /// <summary>Null for the originator; set for invitations.</summary>
    public DateTime? ExpiresAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    protected FundGuarantor() { }

    public bool IsActive => Status == FundGuarantorStatus.Active;

    public bool IsOriginator => Role == FundGuarantorRole.Originator;

    /// <summary>An open invitation that has not expired yet.</summary>
    public bool IsOpenInvitation =>
        Status == FundGuarantorStatus.Invited
        && (ExpiresAtUtc is null || ExpiresAtUtc > DateTime.UtcNow);

    public bool IsExpired =>
        Status == FundGuarantorStatus.Invited
        && ExpiresAtUtc is not null
        && ExpiresAtUtc <= DateTime.UtcNow;

    /// <summary>
    /// Creates the Fund Originator's active membership row. Called once, when a
    /// Group Guarantor fund is created.
    /// </summary>
    public static FundGuarantor CreateOriginator(
        Guid fundId,
        Guid originatorId,
        decimal committedCapital)
    {
        if (fundId == Guid.Empty)
        {
            throw new InvalidFundGuarantorException("A fund is required.");
        }

        if (originatorId == Guid.Empty)
        {
            throw new InvalidFundGuarantorException("A Fund Originator is required.");
        }

        EnsureValidCommitment(committedCapital);

        return new FundGuarantor
        {
            Id = Guid.NewGuid(),
            FundId = fundId,
            GuarantorId = originatorId,
            Role = FundGuarantorRole.Originator,
            Status = FundGuarantorStatus.Active,
            CommittedCapital = committedCapital,
            InvitedEmail = string.Empty,
            InvitedByUserId = originatorId,
            InvitedAtUtc = DateTime.UtcNow,
            RespondedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = null,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Invites an existing, approved Guarantor account to operate the fund.
    /// </summary>
    public static FundGuarantor Invite(
        Guid fundId,
        Guid guarantorId,
        string invitedEmail,
        decimal committedCapital,
        Guid invitedByUserId)
    {
        if (fundId == Guid.Empty)
        {
            throw new InvalidFundGuarantorException("A fund is required.");
        }

        if (guarantorId == Guid.Empty)
        {
            throw new InvalidFundGuarantorException("An invited Guarantor is required.");
        }

        if (string.IsNullOrWhiteSpace(invitedEmail))
        {
            throw new InvalidFundGuarantorException("The invited Guarantor's email is required.");
        }

        if (invitedByUserId == Guid.Empty)
        {
            throw new InvalidFundGuarantorException("The inviting Guarantor is required.");
        }

        EnsureValidCommitment(committedCapital);

        var now = DateTime.UtcNow;
        return new FundGuarantor
        {
            Id = Guid.NewGuid(),
            FundId = fundId,
            GuarantorId = guarantorId,
            Role = FundGuarantorRole.Guarantor,
            Status = FundGuarantorStatus.Invited,
            CommittedCapital = committedCapital,
            InvitedEmail = invitedEmail.Trim().ToLowerInvariant(),
            InvitedByUserId = invitedByUserId,
            InvitedAtUtc = now,
            ExpiresAtUtc = now.AddDays(InvitationLifetimeDays),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    /// <summary>
    /// Re-opens a previously declined, revoked, removed or expired invitation.
    /// The Guarantor keeps their identity; only the lifecycle restarts.
    /// </summary>
    public void ReinstateInvitation(decimal committedCapital, Guid invitedByUserId)
    {
        if (Status == FundGuarantorStatus.Active)
        {
            throw new InvalidFundGuarantorException("That Guarantor is already an active member of this fund.");
        }

        if (IsOriginator)
        {
            throw new InvalidFundGuarantorException("The Fund Originator cannot be re-invited.");
        }

        EnsureValidCommitment(committedCapital);

        var now = DateTime.UtcNow;
        Role = FundGuarantorRole.Guarantor;
        Status = FundGuarantorStatus.Invited;
        CommittedCapital = committedCapital;
        InvitedByUserId = invitedByUserId;
        InvitedAtUtc = now;
        RespondedAtUtc = null;
        ExpiresAtUtc = now.AddDays(InvitationLifetimeDays);
        Touch();
    }

    /// <summary>Accepts an open invitation and becomes an active Group Guarantor.</summary>
    public void Accept()
    {
        if (Status != FundGuarantorStatus.Invited)
        {
            throw new InvalidFundGuarantorException("Only a pending invitation can be accepted.");
        }

        if (IsExpired)
        {
            throw new InvalidFundGuarantorException("This invitation has expired.");
        }

        Status = FundGuarantorStatus.Active;
        RespondedAtUtc = DateTime.UtcNow;
        ExpiresAtUtc = null;
        Touch();
    }

    /// <summary>Declines an open invitation. Terminal for this invitation.</summary>
    public void Decline()
    {
        if (Status != FundGuarantorStatus.Invited)
        {
            throw new InvalidFundGuarantorException("Only a pending invitation can be declined.");
        }

        Status = FundGuarantorStatus.Declined;
        RespondedAtUtc = DateTime.UtcNow;
        Touch();
    }

    /// <summary>Marks an open invitation as expired.</summary>
    public void MarkExpired()
    {
        if (Status != FundGuarantorStatus.Invited)
        {
            throw new InvalidFundGuarantorException("Only a pending invitation can expire.");
        }

        Status = FundGuarantorStatus.Expired;
        RespondedAtUtc = DateTime.UtcNow;
        Touch();
    }

    /// <summary>
    /// The Fund Originator withdraws an invitation or removes an active
    /// co-guarantor. The Fund Originator cannot revoke themselves.
    /// </summary>
    public void Revoke()
    {
        if (IsOriginator)
        {
            throw new InvalidFundGuarantorException("The Fund Originator cannot be removed from the fund.");
        }

        if (Status is not (FundGuarantorStatus.Invited or FundGuarantorStatus.Active))
        {
            throw new InvalidFundGuarantorException("Only a pending invitation or an active membership can be revoked.");
        }

        Status = FundGuarantorStatus.Revoked;
        RespondedAtUtc = DateTime.UtcNow;
        Touch();
    }

    /// <summary>An active co-guarantor leaves the group voluntarily.</summary>
    public void Leave()
    {
        if (IsOriginator)
        {
            throw new InvalidFundGuarantorException("The Fund Originator cannot leave the fund.");
        }

        if (Status != FundGuarantorStatus.Active)
        {
            throw new InvalidFundGuarantorException("Only an active Group Guarantor can leave.");
        }

        Status = FundGuarantorStatus.Removed;
        RespondedAtUtc = DateTime.UtcNow;
        Touch();
    }

    /// <summary>
    /// Changes this Guarantor's own committed capital. The caller is responsible
    /// for checking that the fund's aggregate stays above active disbursed loans
    /// and for keeping <see cref="Fund.CommittedCapital"/> in sync.
    /// </summary>
    public void SetCommittedCapital(decimal committedCapital)
    {
        EnsureValidCommitment(committedCapital);
        CommittedCapital = committedCapital;
        Touch();
    }

    private static void EnsureValidCommitment(decimal committedCapital)
    {
        if (committedCapital < 0)
        {
            throw new InvalidFundGuarantorException("Committed capital cannot be negative.");
        }
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}

/// <summary>The Guarantor's role within a Group Guarantor fund.</summary>
public enum FundGuarantorRole
{
    /// <summary>The Guarantor who created the fund.</summary>
    Originator = 1,

    /// <summary>An invited co-guarantor.</summary>
    Guarantor = 2,
}

/// <summary>Lifecycle state of a Guarantor's relationship to a group fund.</summary>
public enum FundGuarantorStatus
{
    /// <summary>Invited, awaiting a decision.</summary>
    Invited = 1,

    /// <summary>Accepted and jointly operating the fund.</summary>
    Active = 2,

    /// <summary>The invited Guarantor declined.</summary>
    Declined = 3,

    /// <summary>Withdrawn by the Fund Originator.</summary>
    Revoked = 4,

    /// <summary>Left the group voluntarily.</summary>
    Removed = 5,

    /// <summary>The invitation passed its expiry date without a decision.</summary>
    Expired = 6,
}
