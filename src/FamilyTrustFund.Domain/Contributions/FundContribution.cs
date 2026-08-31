namespace FamilyTrustFund.Domain.Contributions;

/// <summary>
/// A Family fund contribution reported by a member.
/// </summary>
/// <remarks>
/// A contribution is a financial transaction and must have an auditable record.
/// A member's contribution is their continuing stake / Fund Credit; it is not
/// collateral consumed by borrowing (ADR-005) and it is held separately from
/// the Guarantor's committed lending capital (ADR-004, ADR-018).
///
/// Only a confirmed contribution posts to the ledger. A member with an active
/// loan cannot contribute or increase their Fund Credit (ADR-006).
/// </remarks>
public class FundContribution
{
    public Guid Id { get; private set; }
    public Guid FundId { get; private set; }
    public Guid MemberId { get; private set; }

    /// <summary>Amount of the contribution in NGN.</summary>
    public decimal Amount { get; private set; }

    public ContributionStatus Status { get; private set; }

    /// <summary>Optional reference supplied by the member (e.g. bank reference).</summary>
    public string? Reference { get; private set; }

    /// <summary>Optional free-text note from the member.</summary>
    public string? Note { get; private set; }

    /// <summary>Rejection reason supplied by the Guarantor.</summary>
    public string? RejectionReason { get; private set; }

    public DateTime ReportedAtUtc { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public DateTime? RejectedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    protected FundContribution() { }

    /// <summary>
    /// Reports a new contribution by a member. The contribution starts in
    /// <see cref="ContributionStatus.PendingConfirmation"/> and does not affect
    /// Fund Credit until the Guarantor confirms it.
    /// </summary>
    public static FundContribution Report(
        Guid fundId,
        Guid memberId,
        decimal amount,
        string? reference = null,
        string? note = null)
    {
        if (fundId == Guid.Empty)
        {
            throw new InvalidContributionException("Fund is required.");
        }

        if (memberId == Guid.Empty)
        {
            throw new InvalidContributionException("Member is required.");
        }

        if (amount <= 0)
        {
            throw new InvalidContributionException("Contribution amount must be greater than zero.");
        }

        if (reference is not null && reference.Length > 200)
        {
            throw new InvalidContributionException("Reference cannot exceed 200 characters.");
        }

        if (note is not null && note.Length > 1000)
        {
            throw new InvalidContributionException("Note cannot exceed 1000 characters.");
        }

        return new FundContribution
        {
            Id = Guid.NewGuid(),
            FundId = fundId,
            MemberId = memberId,
            Amount = amount,
            Status = ContributionStatus.PendingConfirmation,
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ReportedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Guarantor confirms a pending contribution, posting it to Fund Credit.
    /// </summary>
    public void Confirm()
    {
        if (Status != ContributionStatus.PendingConfirmation)
        {
            throw new InvalidContributionException("Only pending contributions can be confirmed.");
        }

        Status = ContributionStatus.Confirmed;
        ConfirmedAtUtc = DateTime.UtcNow;
        Touch();
    }

    /// <summary>
    /// Guarantor rejects a pending contribution. A rejected contribution never
    /// affects Fund Credit.
    /// </summary>
    public void Reject(string? reason = null)
    {
        if (Status != ContributionStatus.PendingConfirmation)
        {
            throw new InvalidContributionException("Only pending contributions can be rejected.");
        }

        if (reason is not null && reason.Length > 1000)
        {
            throw new InvalidContributionException("Rejection reason cannot exceed 1000 characters.");
        }

        Status = ContributionStatus.Rejected;
        RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        RejectedAtUtc = DateTime.UtcNow;
        Touch();
    }

    public bool IsConfirmed => Status == ContributionStatus.Confirmed;

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
