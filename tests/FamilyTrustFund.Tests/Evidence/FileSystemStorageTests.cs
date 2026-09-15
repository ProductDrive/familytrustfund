using FamilyTrustFund.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace FamilyTrustFund.Tests.Evidence;

public class FileSystemStorageTests
{
    private static FileSystemStorage CreateStorage(out string rootPath)
    {
        var root = Path.Combine(Path.GetTempPath(), "familytrustfund-tests", Guid.NewGuid().ToString("N"));
        rootPath = root;
        var options = Options.Create(new FileSystemStorageOptions { RootPath = root });
        return new FileSystemStorage(options);
    }

    [Fact]
    public async Task Store_Read_Delete_Roundtrip()
    {
        var storage = CreateStorage(out var root);
        try
        {
            var content = new byte[] { 1, 2, 3, 4, 5, 6 };

            var stored = await storage.StoreAsync("contribution", content, "image/png");
            stored.ObjectKey.Should().NotBeNullOrWhiteSpace();
            stored.ContentType.Should().Be("image/png");
            stored.SizeBytes.Should().Be(6);

            var read = await storage.ReadAsync("contribution", stored.ObjectKey);
            read.Should().NotBeNull();
            read!.Content.Should().BeEquivalentTo(content);
            read.ContentType.Should().Be("image/png");

            var deleted = await storage.DeleteAsync("contribution", stored.ObjectKey);
            deleted.Should().BeTrue();

            var afterDelete = await storage.ReadAsync("contribution", stored.ObjectKey);
            afterDelete.Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Store_GeneratesUniqueKeys()
    {
        var storage = CreateStorage(out var root);
        try
        {
            var a = await storage.StoreAsync("contribution", new byte[] { 1 }, "image/png");
            var b = await storage.StoreAsync("contribution", new byte[] { 2 }, "image/png");

            a.ObjectKey.Should().NotBe(b.ObjectKey);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Read_ReturnsNullWhenMissing()
    {
        var storage = CreateStorage(out var root);
        try
        {
            var result = await storage.ReadAsync("contribution", "does-not-exist");
            result.Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PathTraversalKey_DoesNotEscapeRoot()
    {
        var storage = CreateStorage(out var root);
        try
        {
            await storage.StoreAsync("contribution", new byte[] { 1 }, "image/png");
            // Store under container ".." which sanitizes to "__"; read must not
            // be able to escape the root directory. Reading a traversal-ish key
            // within the sanitized path should not touch anything outside.
            var result = await storage.ReadAsync("..\\..\\contribution", "..\\..\\..\\etc\\passwd");
            result.Should().BeNull();

            // Ensure nothing was written outside the root.
            var parent = Path.GetDirectoryName(root)!;
            Directory.GetFiles(parent, "passwd", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
