using FamilyTrustFund.Application.Audit;
using FamilyTrustFund.Application.Contributions;
using FamilyTrustFund.Application.Funds;
using FamilyTrustFund.Application.Loans;
using FamilyTrustFund.Application.Membership;
using FamilyTrustFund.Application.Payments;
using FamilyTrustFund.Application.Repayments;
using FamilyTrustFund.Domain.Contributions;
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

    public Task<bool> JoinCodeExistsAsync(Guid guarantorId, string joinCode, CancellationToken ct = default) =>
        Task.FromResult(Funds.Any(f => f.GuarantorId == guarantorId && f.JoinCode == joinCode.Trim().ToUpperInvariant()));

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

    public List<LoanWithDetails> LoanDetails { get; set; } = new();

    public Dictionary<(Guid MemberId, Guid FundId), int> ActiveDisbursedCounts { get; set; } = new();

    public Dictionary<Guid, decimal> DisbursedTotals { get; set; } = new();

    public HashSet<(Guid MemberId, Guid FundId)> PendingRequests { get; set; } = new();

    public Task<Loan?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Loans.FirstOrDefault(l => l.Id == id));

    public Task<IReadOnlyList<LoanWithDetails>> GetPendingForGuarantorAsync(
        Guid guarantorId,
        CancellationToken ct = default)
    {
        var pending = Loans
            .Where(l => l.Status == LoanStatus.Pending)
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

    public Task<int> CountActiveDisbursedByMemberInFundAsync(
        Guid memberId,
        Guid fundId,
        CancellationToken ct = default)
    {
        var key = (memberId, fundId);
        return Task.FromResult(
            ActiveDisbursedCounts.TryGetValue(key, out var count) ? count : 0);
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
        Guid fundId,
        CancellationToken ct = default) =>
        Task.FromResult(ActiveLoans.Contains((memberId, fundId)));

    public void Add(FundContribution contribution) => Contributions.Add(contribution);

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakePaymentRepository : IPaymentRepository
{
    public List<PaymentRecipient> Recipients { get; } = new();
    public List<DisbursementTransaction> Transactions { get; } = new();

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

public sealed class FakePaymentProvider : IPaymentProvider
{
    public string Name { get; set; } = "Paystack";
    public string RecipientCode { get; set; } = "RCP_abc123";
    public string TransferReference { get; set; } = "TRF_xyz789";
    public bool RecipientFail { get; set; }
    public bool TransferFail { get; set; }

    public Task<PaymentProviderResult> CreateRecipientAsync(
        CreateRecipientRequest request,
        CancellationToken ct = default) =>
        Task.FromResult(RecipientFail
            ? PaymentProviderResult.Fail("Recipient verification failed.")
            : PaymentProviderResult.Ok(RecipientCode));

    public Task<PaymentProviderResult> InitiateTransferAsync(
        InitiateTransferRequest request,
        CancellationToken ct = default) =>
        Task.FromResult(TransferFail
            ? PaymentProviderResult.Fail("Transfer initiation failed.")
            : PaymentProviderResult.Ok(TransferReference));
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

    public Task AddAsync(LoanSchedule schedule, CancellationToken ct = default)
    {
        Schedules.Add(schedule);
        return Task.CompletedTask;
    }

    public Task AddAsync(Repayment repayment, CancellationToken ct = default)
    {
        Repayments.Add(repayment);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}