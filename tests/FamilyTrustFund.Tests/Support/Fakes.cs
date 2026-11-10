using FamilyTrustFund.Application.Audit;
using FamilyTrustFund.Application.Auth;
using FamilyTrustFund.Application.Contributions;
using FamilyTrustFund.Application.Evidence;
using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Application.Loans;
using FamilyTrustFund.Application.Membership;
using FamilyTrustFund.Application.Notifications;
using FamilyTrustFund.Application.Payments;
using FamilyTrustFund.Application.Repayments;
using FamilyTrustFund.Application.Storage;
using FamilyTrustFund.Domain.Auth;
using FamilyTrustFund.Domain.Contributions;
using FamilyTrustFund.Domain.Evidence;
using FamilyTrustFund.Domain.Funds;
using FamilyTrustFund.Domain.Loans;
using FamilyTrustFund.Domain.Membership;
using FamilyTrustFund.Domain.Payments;
using FamilyTrustFund.Domain.Repayments;

namespace FamilyTrustFund.Tests.Support;

public sealed class FakeAuditLog : IAuditLog
{
    public List<(Guid ActorId, string Action, string ResourceType, Guid? ResourceId)> Events { get; } = new();

    public Task RecordAsync(
        Guid actorId,
        string action,
        string resourceType,
        Guid? resourceId = null,
        string? details = null,
        CancellationToken ct = default)
    {
        Events.Add((actorId, action, resourceType, resourceId));
        return Task.CompletedTask;
    }
}

public sealed class FakeFundRepository : IFundRepository
{
    public List<Fund> Funds { get; } = new();

    public Task<Fund?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Funds.FirstOrDefault(f => f.Id == id));

    public Task<List<Fund>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default) =>
        Task.FromResult(Funds.Where(f => ids.Contains(f.Id)).ToList());

    public Task<List<Fund>> GetByGuarantorAsync(Guid guarantorId, CancellationToken ct = default) =>
        Task.FromResult(Funds.Where(f => f.GuarantorId == guarantorId).ToList());

    public Task<Fund?> GetByJoinCodeAsync(string joinCode, CancellationToken ct = default) =>
        Task.FromResult(Funds.FirstOrDefault(f => f.JoinCode == joinCode.Trim().ToUpperInvariant()));

    public Task<bool> JoinCodeExistsAsync(string joinCode, CancellationToken ct = default) =>
        Task.FromResult(Funds.Any(f => f.JoinCode == joinCode.Trim().ToUpperInvariant()));

    public void Add(Fund fund) => Funds.Add(fund);

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakeMembershipRepository : IMembershipRepository
{
    public List<FundMember> Memberships { get; } = new();

    public Dictionary<Guid, List<FundMemberItem>> MembersByFund { get; } = new();

    public List<AdminMembershipItem> AdminItems { get; } = new();

    public Task<FundMember?> GetByFundAndMemberAsync(Guid fundId, Guid memberId, CancellationToken ct = default) =>
        Task.FromResult(Memberships.FirstOrDefault(m => m.FundId == fundId && m.MemberId == memberId));

    public Task<List<FundMember>> GetByMemberAsync(Guid memberId, CancellationToken ct = default) =>
        Task.FromResult(Memberships.Where(m => m.MemberId == memberId).ToList());

    public Task<List<FundMember>> GetByFundAsync(Guid fundId, CancellationToken ct = default) =>
        Task.FromResult(Memberships.Where(m => m.FundId == fundId).ToList());

    public Task<IReadOnlyList<FundMemberItem>> GetMembersWithUserAsync(Guid fundId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FundMemberItem>>(
            MembersByFund.TryGetValue(fundId, out var items) ? items.ToList() : new List<FundMemberItem>());

    public Task<IReadOnlyList<AdminMembershipItem>> GetAllWithUserAndFundAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AdminMembershipItem>>(AdminItems.ToList());

    public void Add(FundMember fundMember) => Memberships.Add(fundMember);

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakeLoanRepository : ILoanRepository
{
    public List<Loan> Loans { get; } = new();

    public List<LoanApproval> Approvals { get; } = new();

    public List<LoanWithDetails> LoanDetails { get; set; } = new();

    public Dictionary<Guid, decimal> DisbursedTotals { get; set; } = new();

    public HashSet<(Guid MemberId, Guid FundId)> PendingRequests { get; set; } = new();

    public Task<Loan?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Loans.FirstOrDefault(l => l.Id == id));

    public Task<Loan?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Loans.FirstOrDefault(l => l.Id == id));

    public Task<IReadOnlyList<LoanWithDetails>> GetPendingForGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default)
    {
        var pending = Loans
            .Where(l => l.Status == LoanStatus.Pending)
            .Where(l =>
            {
                var approvals = Approvals.Where(a => a.LoanId == l.Id).ToList();
                // Legacy single-Guarantor loans may have no approval rows yet.
                return approvals.Count == 0
                    || approvals.Any(a => a.GuarantorId == guarantorId && a.IsPending);
            })
            .Select(l => new LoanWithDetails
            {
                Loan = l,
                MemberId = l.MemberId,
                FundId = l.FundId,
                GuarantorId = guarantorId,
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<LoanWithDetails>>(pending);
    }

    public Task<IReadOnlyList<LoanWithDetails>> GetByFundAsync(Guid fundId, CancellationToken ct = default)
    {
        var items = Loans
            .Where(l => l.FundId == fundId)
            .Select(l => new LoanWithDetails
            {
                Loan = l,
                MemberId = l.MemberId,
                FundId = l.FundId,
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<LoanWithDetails>>(items);
    }

    public Task<IReadOnlyList<LoanWithDetails>> GetByMemberAsync(Guid memberId, CancellationToken ct = default)
    {
        var items = Loans
            .Where(l => l.MemberId == memberId)
            .Select(l => new LoanWithDetails
            {
                Loan = l,
                MemberId = l.MemberId,
                FundId = l.FundId,
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<LoanWithDetails>>(items);
    }

    public Task<int> CountActiveByMemberAsync(
        Guid memberId,
        CancellationToken ct = default)
    {
        var count = Loans.Count(l => l.MemberId == memberId
            && (l.Status == LoanStatus.Pending
                || l.Status == LoanStatus.Approved
                || l.Status == LoanStatus.Disbursed));
        return Task.FromResult(count);
    }

    public Task<decimal> SumActiveDisbursedByFundAsync(Guid fundId, CancellationToken ct = default)
    {
        return Task.FromResult(
            DisbursedTotals.TryGetValue(fundId, out var total) ? total : 0m);
    }

    public Task<bool> HasPendingRequestAsync(
        Guid memberId,
        Guid fundId,
        CancellationToken ct = default)
    {
        return Task.FromResult(PendingRequests.Contains((memberId, fundId)));
    }

    public Task<bool> HasPendingInFundAsync(Guid fundId, CancellationToken ct = default) =>
        Task.FromResult(Loans.Any(l => l.FundId == fundId && l.Status == LoanStatus.Pending));

    public Task<IReadOnlyList<LoanApproval>> GetApprovalsAsync(
        Guid loanId,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LoanApproval>>(
            Approvals.Where(a => a.LoanId == loanId).ToList());

    public Task<LoanApproval?> GetApprovalAsync(
        Guid loanId,
        Guid guarantorId,
        CancellationToken ct = default) =>
        Task.FromResult(Approvals.FirstOrDefault(
            a => a.LoanId == loanId && a.GuarantorId == guarantorId));

    public Task<IReadOnlyList<LoanApprovalWithDetails>> GetApprovalsWithDetailsAsync(
        IReadOnlyCollection<Guid> loanIds,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LoanApprovalWithDetails>>(
            Approvals
                .Where(a => loanIds.Contains(a.LoanId))
                .Select(a => new LoanApprovalWithDetails
                {
                    Approval = a,
                    GuarantorDisplayName = $"Guarantor {a.GuarantorId:N}"[..16],
                    GuarantorEmail = $"{a.GuarantorId:N}@example.com",
                })
                .ToList());

    public void AddApproval(LoanApproval approval) => Approvals.Add(approval);

    public void AddApprovals(IEnumerable<LoanApproval> approvals) => Approvals.AddRange(approvals);

    public void Add(Loan loan) => Loans.Add(loan);

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakeContributionRepository : IContributionRepository
{
    public List<FundContribution> Contributions { get; } = new();

    public List<ContributionWithDetails> Details { get; set; } = new();

    public Dictionary<(Guid MemberId, Guid FundId), decimal> ConfirmedFundCredits { get; set; } = new();

    public Dictionary<Guid, decimal> ConfirmedByFund { get; set; } = new();

    public HashSet<(Guid MemberId, Guid FundId)> ActiveLoans { get; set; } = new();

    public Task<FundContribution?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Contributions.FirstOrDefault(c => c.Id == id));

    public Task<IReadOnlyList<ContributionWithDetails>> GetPendingForGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default)
    {
        var pending = Contributions
            .Where(c => c.Status == ContributionStatus.PendingConfirmation)
            .Select(c => new ContributionWithDetails
            {
                Contribution = c,
                MemberId = c.MemberId,
                FundId = c.FundId,
                GuarantorId = guarantorId,
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<ContributionWithDetails>>(pending);
    }

    public Task<IReadOnlyList<ContributionWithDetails>> GetByFundAsync(
        Guid fundId,
        CancellationToken ct = default)
    {
        var items = Contributions
            .Where(c => c.FundId == fundId)
            .Select(c => new ContributionWithDetails
            {
                Contribution = c,
                MemberId = c.MemberId,
                FundId = c.FundId,
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<ContributionWithDetails>>(items);
    }

    public Task<IReadOnlyList<ContributionWithDetails>> GetByMemberAsync(
        Guid memberId,
        CancellationToken ct = default)
    {
        var items = Contributions
            .Where(c => c.MemberId == memberId)
            .Select(c => new ContributionWithDetails
            {
                Contribution = c,
                MemberId = c.MemberId,
                FundId = c.FundId,
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<ContributionWithDetails>>(items);
    }

    public Task<decimal> GetConfirmedFundCreditAsync(
        Guid memberId,
        Guid fundId,
        CancellationToken ct = default)
    {
        var key = (memberId, fundId);
        return Task.FromResult(
            ConfirmedFundCredits.TryGetValue(key, out var total) ? total : 0m);
    }

    public Task<decimal> SumConfirmedByFundAsync(
        Guid fundId,
        CancellationToken ct = default) =>
        Task.FromResult(ConfirmedByFund.TryGetValue(fundId, out var total) ? total : 0m);

    public Task<bool> HasActiveDisbursedLoanAsync(
        Guid memberId,
        CancellationToken ct = default) =>
        Task.FromResult(ActiveLoans.Any(a => a.MemberId == memberId));

    public void Add(FundContribution contribution) => Contributions.Add(contribution);

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakeEvidenceRepository : IEvidenceRepository
{
    public List<PaymentEvidence> Items { get; } = new();

    public Task<PaymentEvidence?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(e => e.Id == id));

    public Task<IReadOnlyList<PaymentEvidence>> GetForResourceAsync(
        string resourceType,
        Guid resourceId,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PaymentEvidence>>(
            Items.Where(e => string.Equals(e.ResourceType, resourceType, StringComparison.OrdinalIgnoreCase)
                && e.ResourceId == resourceId)
            .OrderBy(e => e.UploadedAtUtc)
            .ToList());

    public Task<IReadOnlyList<PaymentEvidence>> GetByUploaderAsync(
        Guid uploaderId,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PaymentEvidence>>(
            Items.Where(e => e.UploadedByUserId == uploaderId).ToList());

    public void Add(PaymentEvidence evidence) => Items.Add(evidence);

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakeFileStorage : IFileStorage
{
    public Dictionary<string, byte[]> Objects { get; } = new();

    public Task<StoredObject> StoreAsync(
        string container,
        byte[] content,
        string contentType,
        CancellationToken ct = default)
    {
        var key = $"obj-{Objects.Count + 1}-{contentType}";
        Objects[key] = content;
        return Task.FromResult(new StoredObject
        {
            Container = container,
            ObjectKey = key,
            ContentType = contentType,
            SizeBytes = content.LongLength,
        });
    }

    public Task<StoredObject?> ReadAsync(
        string container,
        string objectKey,
        CancellationToken ct = default)
    {
        if (!Objects.TryGetValue(objectKey, out var content))
        {
            return Task.FromResult<StoredObject?>(null);
        }

        return Task.FromResult<StoredObject?>(new StoredObject
        {
            Container = container,
            ObjectKey = objectKey,
            ContentType = objectKey.Contains("image/png") ? "image/png" : "application/pdf",
            SizeBytes = content.LongLength,
            Content = content,
        });
    }

    public Task<bool> DeleteAsync(string container, string objectKey, CancellationToken ct = default) =>
        Task.FromResult(Objects.Remove(objectKey));
}

public sealed class FakePaymentRepository : IPaymentRepository
{
    public List<PaymentRecipient> Recipients { get; } = new();
    public List<DisbursementTransaction> Transactions { get; } = new();

    /// <summary>Funded-balance consumption per fund (pending + successful transfers).</summary>
    public Dictionary<Guid, decimal> InProgressByFund { get; } = new();

    public Task<PaymentRecipient?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Recipients.FirstOrDefault(r => r.Id == id));

    public Task<PaymentRecipient?> GetActiveRecipientForMemberAsync(
        Guid memberId,
        CancellationToken ct = default) =>
        Task.FromResult(Recipients
            .Where(r => r.MemberId == memberId && r.IsActive)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefault());

    public Task<PaymentRecipient?> GetLatestRecipientForMemberAsync(
        Guid memberId,
        CancellationToken ct = default) =>
        Task.FromResult(Recipients
            .Where(r => r.MemberId == memberId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefault());

    public Task<IReadOnlyList<PaymentRecipient>> GetRecipientsByMemberAsync(
        Guid memberId,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PaymentRecipient>>(
            Recipients.Where(r => r.MemberId == memberId).ToList());

    public Task<DisbursementTransaction?> GetByLoanAsync(Guid loanId, CancellationToken ct = default) =>
        Task.FromResult(Transactions.FirstOrDefault(t => t.LoanId == loanId));

    public Task<DisbursementTransaction?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken ct = default) =>
        Task.FromResult(Transactions.FirstOrDefault(t => t.IdempotencyKey == idempotencyKey));

    public Task<DisbursementTransaction?> GetByProviderReferenceAsync(
        string providerReference,
        CancellationToken ct = default) =>
        Task.FromResult(Transactions.FirstOrDefault(t => t.ProviderReference == providerReference));

    public Task<bool> HasProcessedEventAsync(string eventId, CancellationToken ct = default) =>
        Task.FromResult(Transactions.Any(t => t.LastProcessedEventId == eventId));

    public void AddRecipient(PaymentRecipient recipient) => Recipients.Add(recipient);

    public void AddDisbursement(DisbursementTransaction transaction) => Transactions.Add(transaction);

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakeCapitalFundingRepository : ICapitalFundingRepository
{
    public List<CapitalTransaction> Transactions { get; } = new();

    /// <summary>Confirmed net funding per fund (test seeding of the derived value).</summary>
    public Dictionary<Guid, decimal> ConfirmedNetByFund { get; } = new();

    public Task<CapitalTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Transactions.FirstOrDefault(t => t.Id == id));

    public Task<CapitalTransaction?> GetByProviderReferenceAsync(
        string providerReference,
        CancellationToken ct = default) =>
        Task.FromResult(Transactions.FirstOrDefault(t => t.ProviderReference == providerReference));

    public Task<CapitalTransaction?> GetByLoanAsync(Guid loanId, CancellationToken ct = default) =>
        Task.FromResult(Transactions
            .Where(t => t.LoanId == loanId)
            .OrderByDescending(t => t.InitiatedAtUtc)
            .FirstOrDefault());

    public Task<IReadOnlyList<CapitalTransaction>> GetByFundAsync(
        Guid fundId,
        int page,
        int pageSize,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CapitalTransaction>>(
            Transactions
                .Where(t => t.FundId == fundId)
                .OrderByDescending(t => t.InitiatedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList());

    public Task<int> CountByFundAsync(Guid fundId, CancellationToken ct = default) =>
        Task.FromResult(Transactions.Count(t => t.FundId == fundId));

    public Task<decimal> SumConfirmedNetByFundAsync(Guid fundId, CancellationToken ct = default) =>
        Task.FromResult(ConfirmedNetByFund.TryGetValue(fundId, out var total) ? total : 0m);

    public void Add(CapitalTransaction transaction) => Transactions.Add(transaction);

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakePaymentProvider : IPaymentProvider
{
    public string Name { get; set; } = "Paystack";
    public string RecipientCode { get; set; } = "RCP_abc123";
    public string TransferReference { get; set; } = "TRF_xyz789";
    public bool RecipientFail { get; set; }
    public bool TransferFail { get; set; }

    public bool SubaccountFail { get; set; }
    public string SubaccountCode { get; set; } = "SUB_abc123";

    public decimal? EstimatedFee { get; set; }
    public bool CollectionInitFail { get; set; }
    public string CollectionReference { get; set; } = "COL_chk789";
    public string AuthorizationUrl { get; set; } = "https://paystack.test/checkout";
    public bool VerifyPaid { get; set; } = true;
    public bool VerifyFail { get; set; }
    public decimal VerifyFee { get; set; } = 100m;

    public Task<PaymentProviderResult> CreateRecipientAsync(
        CreateRecipientRequest request,
        CancellationToken ct = default) =>
        Task.FromResult(RecipientFail
            ? PaymentProviderResult.Fail($"Recipient verification failed: {request?.AccountNumber ?? "unknown"}.")
            : PaymentProviderResult.Ok(RecipientCode));

    public Task<PaymentProviderResult> CreateSubaccountAsync(
        CreateSubaccountRequest request,
        CancellationToken ct = default) =>
        Task.FromResult(SubaccountFail
            ? PaymentProviderResult.Fail($"Subaccount creation failed: {request?.AccountNumber ?? "unknown"}.")
            : PaymentProviderResult.Ok(SubaccountCode));

    public Task<PaymentProviderResult> InitiateTransferAsync(
        InitiateTransferRequest request,
        CancellationToken ct = default) =>
        Task.FromResult(TransferFail
            ? PaymentProviderResult.Fail("Transfer initiation failed.")
            : PaymentProviderResult.Ok(TransferReference));

    public Task<CollectionChargeEstimateResult> EstimateCollectionChargeAsync(
        decimal amount,
        string currency,
        CancellationToken ct = default)
    {
        var fee = EstimatedFee ?? CapitalTransaction.ComputeEstimatedFee(amount);
        return Task.FromResult(CollectionChargeEstimateResult.Ok(fee, amount + fee));
    }

    public Task<CollectionInitiationResult> InitializeCollectionAsync(
        CollectionInitiationRequest request,
        CancellationToken ct = default) =>
        Task.FromResult(CollectionInitFail
            ? CollectionInitiationResult.Fail("Collection initiation failed.")
            : CollectionInitiationResult.Ok(CollectionReference, AuthorizationUrl));

    public Task<CollectionVerificationResult> VerifyCollectionAsync(
        string providerReference,
        CancellationToken ct = default)
    {
        if (VerifyFail)
        {
            return Task.FromResult(CollectionVerificationResult.Fail("The payment provider could not be reached. Please try again."));
        }

        return Task.FromResult(VerifyPaid
            ? CollectionVerificationResult.PaidSuccess(0m, VerifyFee)
            : CollectionVerificationResult.NotPaid("Payment status is 'abandoned'."));
    }

    public Task<IReadOnlyList<BankInfo>> GetBanksAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BankInfo>>(new List<BankInfo>
        {
            new("011", "First Bank of Nigeria"),
            new("058", "Guaranty Trust Bank"),
            new("070", "Fidelity Bank"),
            new("057", "Zenith Bank"),
        });
}

public sealed class FakePaymentProviderRegistry : IPaymentProviderRegistry
{
    public Dictionary<string, IPaymentProvider> ByName { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IPaymentProvider? Get(string providerName) =>
        ByName.TryGetValue(providerName, out var provider) ? provider : null;
}

public sealed class FakeRepaymentRepository : IRepaymentRepository
{
    public List<Loan> Loans { get; } = new();
    public List<Fund> Funds { get; } = new();
    public List<LoanSchedule> Schedules { get; } = new();
    public List<Repayment> Repayments { get; } = new();

    public Task<Loan?> GetLoanAsync(Guid loanId, CancellationToken ct = default) =>
        Task.FromResult(Loans.FirstOrDefault(l => l.Id == loanId));

    public Task<LoanSchedule?> GetCurrentScheduleAsync(Guid loanId, CancellationToken ct = default) =>
        Task.FromResult(Schedules
            .Where(s => s.LoanId == loanId)
            .OrderByDescending(s => s.Version)
            .FirstOrDefault());

    public Task<IReadOnlyList<LoanSchedule>> GetAllSchedulesAsync(Guid loanId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LoanSchedule>>(
            Schedules.Where(s => s.LoanId == loanId).OrderBy(s => s.Version).ToList());

    public Task<LoanScheduleItem?> GetItemAsync(Guid itemId, CancellationToken ct = default) =>
        Task.FromResult(Schedules.SelectMany(s => s.Items).FirstOrDefault(i => i.Id == itemId));

    public Task<IReadOnlyList<Repayment>> GetRepaymentsAsync(Guid loanId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Repayment>>(
            Repayments.Where(r => r.LoanId == loanId).ToList());

    public Task<(IReadOnlyList<RepaymentWithFund> Items, int TotalCount)> GetMyRepaymentsAsync(
        Guid memberId, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;

        var loanIds = Loans.Where(l => l.MemberId == memberId).Select(l => l.Id).ToHashSet();
        var ordered = Repayments
            .Where(r => loanIds.Contains(r.LoanId))
            .OrderByDescending(r => r.PaidAtUtc)
            .ToList();

        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r =>
            {
                var loan = Loans.FirstOrDefault(l => l.Id == r.LoanId);
                var fund = loan is null ? null : Funds.FirstOrDefault(f => f.Id == loan.FundId);
                return new RepaymentWithFund
                {
                    Repayment = r,
                    FundName = fund?.Name ?? string.Empty,
                };
            })
            .ToList();

        IReadOnlyList<RepaymentWithFund> result = items;
        return Task.FromResult((result, ordered.Count));
    }

    public Task AddAsync(LoanSchedule schedule, CancellationToken ct = default)
    {
        Schedules.Add(schedule);
        return Task.CompletedTask;
    }

    public Task<LoanSchedule?> SaveNewScheduleAsync(LoanSchedule schedule, CancellationToken ct = default)
    {
        var existing = Schedules.FirstOrDefault(s =>
            s.LoanId == schedule.LoanId && s.Version == schedule.Version);
        if (existing is not null)
        {
            return Task.FromResult<LoanSchedule?>(existing);
        }

        Schedules.Add(schedule);
        return Task.FromResult<LoanSchedule?>(schedule);
    }

    public Task AddAsync(Repayment repayment, CancellationToken ct = default)
    {
        Repayments.Add(repayment);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakePendingRepaymentRepository : IPendingRepaymentRepository
{
    public List<PendingRepayment> Items { get; } = new();

    public List<PendingRepaymentWithDetails> Details { get; set; } = new();

    public Task<PendingRepayment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.Id == id));

    public Task<bool> HasPendingForLoanAsync(Guid loanId, CancellationToken ct = default) =>
        Task.FromResult(Items.Any(p => p.LoanId == loanId && p.Status == PendingRepaymentStatus.PendingConfirmation));

    public Task<bool> HasPendingByMemberAndFundAsync(
        Guid memberId,
        Guid fundId,
        CancellationToken ct = default) =>
        Task.FromResult(Items.Any(p => p.MemberId == memberId && p.Status == PendingRepaymentStatus.PendingConfirmation));

    public Task<IReadOnlyList<PendingRepaymentWithDetails>> GetPendingForGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PendingRepaymentWithDetails>>(
            Details.Where(d => d.Pending.Status == PendingRepaymentStatus.PendingConfirmation
                && d.GuarantorId == guarantorId).ToList());

    public Task<IReadOnlyList<PendingRepaymentWithDetails>> GetByMemberAsync(
        Guid memberId,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PendingRepaymentWithDetails>>(
            Details.Where(d => d.MemberId == memberId).ToList());

    public void Add(PendingRepayment pending) => Items.Add(pending);

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakeLoginOtpRepository : ILoginOtpRepository
{
    public List<LoginOtp> Items { get; } = new();

    public Task<LoginOtp?> GetLatestByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        Task.FromResult(Items
            .Where(o => o.Email == normalizedEmail)
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefault());

    public Task<LoginOtp?> GetLatestUnconsumedAsync(string normalizedEmail, CancellationToken ct = default) =>
        Task.FromResult(Items
            .Where(o => o.Email == normalizedEmail && o.ConsumedAtUtc == null)
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefault());

    public Task<IReadOnlyList<LoginOtp>> GetUnconsumedByEmailAsync(
        string normalizedEmail,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LoginOtp>>(
            Items.Where(o => o.Email == normalizedEmail && o.ConsumedAtUtc == null).ToList());

    public void Add(LoginOtp otp) => Items.Add(otp);

    public void RemoveRange(IEnumerable<LoginOtp> otps)
    {
        foreach (var otp in otps.ToList())
        {
            Items.Remove(otp);
        }
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakeUserAccountLookup : IUserAccountLookup
{
    public HashSet<string> ExistingEmails { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> ExistsAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(ExistingEmails.Contains(email));
}

public sealed class FakeTermsAcceptanceRepository : ITermsAcceptanceRepository
{
    public List<TermsAcceptance> Items { get; } = new();

    public Task<bool> ExistsAsync(Guid userId, string version, CancellationToken ct = default) =>
        Task.FromResult(Items.Any(t => t.UserId == userId && t.Version == version));

    public void Add(TermsAcceptance acceptance) => Items.Add(acceptance);

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = new();

    public Exception? FailWith { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        Sent.Add(message);
        return Task.CompletedTask;
    }
}
public sealed class FakeGuarantorRoleChecker : FamilyTrustFund.Application.Membership.IGuarantorRoleChecker
{
    public bool Guarantor { get; set; }
    public Task<bool> IsGuarantorAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(Guarantor);
}

public sealed class FakeFundGuarantorRepository : IFundGuarantorRepository
{
    private readonly FakeFundRepository? _funds;

    public FakeFundGuarantorRepository(FakeFundRepository? funds = null)
    {
        _funds = funds;
    }

    public List<FundGuarantor> Memberships { get; } = new();

    public List<GuarantorAccount> Accounts { get; } = new();

    public Dictionary<Guid, GuarantorProfile> Profiles { get; } = new();

    public Task<FundGuarantor?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Memberships.FirstOrDefault(m => m.Id == id));

    public Task<FundGuarantor?> GetAsync(
        Guid fundId,
        Guid guarantorId,
        CancellationToken ct = default) =>
        Task.FromResult(Memberships.FirstOrDefault(
            m => m.FundId == fundId && m.GuarantorId == guarantorId));

    public Task<IReadOnlyList<FundGuarantor>> GetByFundAsync(
        Guid fundId,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FundGuarantor>>(
            Memberships.Where(m => m.FundId == fundId).ToList());

    public Task<IReadOnlyList<FundGuarantor>> GetActiveByFundAsync(
        Guid fundId,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FundGuarantor>>(
            Memberships.Where(m => m.FundId == fundId && m.IsActive).ToList());

    public Task<IReadOnlyList<FundGuarantorWithDetails>> GetByGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default)
    {
        var items = Memberships
            .Where(m => m.GuarantorId == guarantorId)
            .Select(m =>
            {
                var fund = _funds?.Funds.FirstOrDefault(f => f.Id == m.FundId);
                var originatorId = fund?.GuarantorId ?? Guid.Empty;
                Profiles.TryGetValue(originatorId, out var originator);
                return new FundGuarantorWithDetails
                {
                    Membership = m,
                    FundId = m.FundId,
                    FundName = fund?.Name ?? string.Empty,
                    FundType = fund?.Type ?? FundType.Family,
                    OriginatorId = originatorId,
                    OriginatorDisplayName = originator?.DisplayName ?? string.Empty,
                    OriginatorEmail = originator?.Email ?? string.Empty,
                };
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<FundGuarantorWithDetails>>(items);
    }

    public Task<int> CountActiveAsync(Guid fundId, CancellationToken ct = default) =>
        Task.FromResult(Memberships.Count(m => m.FundId == fundId && m.IsActive));

    public Task<GuarantorAccount?> FindGuarantorByEmailAsync(
        string email,
        CancellationToken ct = default) =>
        Task.FromResult(Accounts.FirstOrDefault(a =>
            string.Equals(a.Email, email.Trim(), StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyDictionary<Guid, GuarantorProfile>> GetProfilesAsync(
        IEnumerable<Guid> guarantorIds,
        CancellationToken ct = default)
    {
        var ids = guarantorIds.ToHashSet();
        var result = Profiles
            .Where(kv => ids.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        return Task.FromResult<IReadOnlyDictionary<Guid, GuarantorProfile>>(result);
    }

    public void Add(FundGuarantor membership) => Memberships.Add(membership);

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakeFundAccessService : IFundAccessService
{
    private readonly FakeFundRepository _funds;
    private readonly FakeFundGuarantorRepository _guarantors;

    public FakeFundAccessService(
        FakeFundRepository funds,
        FakeFundGuarantorRepository? guarantors = null)
    {
        _funds = funds;
        _guarantors = guarantors ?? new FakeFundGuarantorRepository(funds);
    }

    public Task<bool> CanAccessFundAsync(
        Fund fund,
        Guid userId,
        CancellationToken ct = default)
    {
        if (fund.GuarantorId == userId)
        {
            return Task.FromResult(true);
        }

        if (!fund.IsGroupFund)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_guarantors.Memberships.Any(m =>
            m.FundId == fund.Id && m.GuarantorId == userId && m.IsActive));
    }

    public async Task<bool> CanAccessFundAsync(
        Guid fundId,
        Guid userId,
        CancellationToken ct = default)
    {
        var fund = await _funds.GetByIdAsync(fundId, ct);
        return fund is not null && await CanAccessFundAsync(fund, userId, ct);
    }

    public async Task<bool> IsOriginatorAsync(
        Guid fundId,
        Guid userId,
        CancellationToken ct = default)
    {
        var fund = await _funds.GetByIdAsync(fundId, ct);
        return fund is not null && fund.GuarantorId == userId;
    }

    public Task<IReadOnlyList<Guid>> GetRequiredApproverIdsAsync(
        Fund fund,
        CancellationToken ct = default)
    {
        if (!fund.IsGroupFund)
        {
            return Task.FromResult<IReadOnlyList<Guid>>(new[] { fund.GuarantorId });
        }

        var ids = _guarantors.Memberships
            .Where(m => m.FundId == fund.Id && m.IsActive)
            .Select(m => m.GuarantorId)
            .ToList();

        // The Fund Originator always decides, even if a membership row is missing.
        if (!ids.Contains(fund.GuarantorId))
        {
            ids.Insert(0, fund.GuarantorId);
        }

        return Task.FromResult<IReadOnlyList<Guid>>(ids);
    }
}
