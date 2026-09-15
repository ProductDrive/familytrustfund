using FamilyTrustFund.Application.Evidence;
using FamilyTrustFund.Domain.Evidence;
using FamilyTrustFund.Tests.Support;
using FluentAssertions;

namespace FamilyTrustFund.Tests.Evidence;

public class EvidenceServiceTests
{
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid ContributionId = Guid.NewGuid();

    private static (
        FakeEvidenceRepository evidence,
        FakeFileStorage storage,
        FakeAuditLog audit,
        EvidenceService service) Setup()
    {
        var evidence = new FakeEvidenceRepository();
        var storage = new FakeFileStorage();
        var audit = new FakeAuditLog();
        var service = new EvidenceService(evidence, storage, audit);
        return (evidence, storage, audit, service);
    }

    [Fact]
    public async Task Upload_RegistersEvidenceAndStoresContent()
    {
        var (evidence, storage, audit, service) = Setup();
        var content = new byte[] { 1, 2, 3, 4 };

        var dto = await service.UploadAsync(MemberId, "Contribution", ContributionId, "receipt.png", "image/png", content);

        dto.ResourceType.Should().Be("Contribution");
        dto.ResourceId.Should().Be(ContributionId);
        dto.OriginalFileName.Should().Be("receipt.png");
        dto.ContentType.Should().Be("image/png");
        dto.SizeBytes.Should().Be(4);
        dto.UploadedByUserId.Should().Be(MemberId);

        evidence.Items.Should().ContainSingle();
        storage.Objects.Should().HaveCount(1);
        audit.Events.Should().Contain(e => e.Action == "Evidence.Uploaded");
    }

    [Fact]
    public async Task Upload_RejectsUnallowedContentType()
    {
        var (_, _, _, service) = Setup();

        var act = () => service.UploadAsync(MemberId, "Contribution", ContributionId, "malware.exe", "application/x-msdownload", new byte[] { 1 });

        await act.Should().ThrowAsync<InvalidEvidenceException>()
            .WithMessage("*not allowed*");
    }

    [Fact]
    public async Task Upload_RejectsEmptyContent()
    {
        var (_, _, _, service) = Setup();

        var act = () => service.UploadAsync(MemberId, "Contribution", ContributionId, "empty.png", "image/png", Array.Empty<byte>());

        await act.Should().ThrowAsync<InvalidEvidenceException>();
    }

    [Fact]
    public async Task Upload_RejectsOversizedContent()
    {
        var (_, _, _, service) = Setup();
        var big = new byte[(5 * 1024 * 1024) + 1];

        var act = () => service.UploadAsync(MemberId, "Contribution", ContributionId, "big.png", "image/png", big);

        await act.Should().ThrowAsync<InvalidEvidenceException>()
            .WithMessage("*too large*");
    }

    [Fact]
    public async Task GetForResource_ReturnsOnlyMatchingEvidence()
    {
        var (evidence, _, _, service) = Setup();
        await service.UploadAsync(MemberId, "Contribution", ContributionId, "a.png", "image/png", new byte[] { 1 });
        await service.UploadAsync(MemberId, "Contribution", Guid.NewGuid(), "b.png", "image/png", new byte[] { 2 });

        var result = await service.GetForResourceAsync("Contribution", ContributionId);

        result.Should().ContainSingle().Which.ResourceId.Should().Be(ContributionId);
    }

    [Fact]
    public async Task Read_ReturnsNullWhenEvidenceMissing()
    {
        var (_, _, _, service) = Setup();

        var result = await service.ReadAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task Read_ReturnsStoredContent()
    {
        var (_, storage, _, service) = Setup();
        var content = new byte[] { 9, 8, 7 };
        var dto = await service.UploadAsync(MemberId, "Contribution", ContributionId, "receipt.png", "image/png", content);

        var result = await service.ReadAsync(dto.Id);

        result.Should().NotBeNull();
        result!.Value.Evidence.OriginalFileName.Should().Be("receipt.png");
        result.Value.Storage.Content.Should().BeEquivalentTo(content);
    }

    [Fact]
    public async Task MarkReviewed_RecordsReviewerOnConfirmedResource()
    {
        var reviewerId = Guid.NewGuid();
        var evidence = PaymentEvidence.Register(MemberId, "Contribution", ContributionId, "contribution", "k", "a.png", "image/png", 5);

        evidence.MarkReviewed(reviewerId);

        evidence.ReviewedByUserId.Should().Be(reviewerId);
        evidence.ReviewedAtUtc.Should().NotBeNull();
    }
}
