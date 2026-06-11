using BannerShop.Api.Services.BannerBuilder;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Unit tests for BannerFileStorage (pure path-computation helpers).
/// </summary>
public class BannerFileStorageTests
{
    private static BannerFileStorage CreateStorage(
        string localRoot = "/tmp/test",
        string publicBase = "https://cdn.example.com/uploads")
    {
        var opts = Options.Create(new FileStorageOptions
        {
            LocalRoot     = localRoot,
            PublicBaseUrl = publicBase,
            MaxUploadBytes = 50L * 1024 * 1024
        });
        return new BannerFileStorage(opts);
    }

    // ── PublicUrlFor ──────────────────────────────────────────────────────────

    [Fact]
    public void PublicUrlFor_RelativePath_ReturnsFullUrl()
    {
        var storage = CreateStorage(publicBase: "https://cdn.example.com/files");

        var url = storage.PublicUrlFor("banner-builder/1/test.jpg");

        url.Should().Be("https://cdn.example.com/files/banner-builder/1/test.jpg");
    }

    [Fact]
    public void PublicUrlFor_PublicBaseWithTrailingSlash_NormalisesCorrectly()
    {
        var storage = CreateStorage(publicBase: "https://cdn.example.com/files/");

        var url = storage.PublicUrlFor("banner-builder/1/test.jpg");

        // TrimEnd('/') prevents double-slash
        url.Should().NotContain("//banner-builder");
        url.Should().Contain("banner-builder/1/test.jpg");
    }

    // ── AbsolutePathFor ───────────────────────────────────────────────────────

    [Fact]
    public void AbsolutePathFor_RelativePath_CombinesWithLocalRoot()
    {
        var storage = CreateStorage(localRoot: "/var/data/bannershop");

        var abs = storage.AbsolutePathFor("banner-builder/1/file.jpg");

        abs.Should().StartWith("/var/data/bannershop");
        abs.Should().Contain("file.jpg");
    }

    // ── RelativePathFor (static) ──────────────────────────────────────────────

    [Fact]
    public void RelativePathFor_AuthenticatedUser_IncludesUserId()
    {
        var path = BannerFileStorage.RelativePathFor(userId: 42, "test.jpg");

        path.Should().Be("banner-builder/42/test.jpg");
    }

    [Fact]
    public void RelativePathFor_AnonymousUser_UsesZeroDirectory()
    {
        var path = BannerFileStorage.RelativePathFor(userId: null, "anon.jpg");

        path.Should().Be("banner-builder/0/anon.jpg");
    }

    // ── NewFileName (static) ──────────────────────────────────────────────────

    [Fact]
    public void NewFileName_GeneratesGuidBasedName()
    {
        var name = BannerFileStorage.NewFileName("jpg");

        name.Should().EndWith(".jpg");
        name.Should().HaveLength(36); // 32-char GUID + ".jpg" = 36
    }

    [Fact]
    public void NewFileName_StripsDotFromExtension()
    {
        var name = BannerFileStorage.NewFileName(".png");

        name.Should().EndWith(".png");
        name.Should().NotContain("..");
    }

    [Fact]
    public void NewFileName_UppercaseExtension_NormalisesToLowercase()
    {
        var name = BannerFileStorage.NewFileName("PNG");

        name.Should().EndWith(".png");
    }

    [Fact]
    public void NewFileName_EmptyExtension_UsesEmptyDot()
    {
        var name = BannerFileStorage.NewFileName(string.Empty);

        name.Should().NotBeNullOrEmpty();
    }

    // ── TryDelete ─────────────────────────────────────────────────────────────

    [Fact]
    public void TryDelete_NullPath_DoesNotThrow()
    {
        var storage = CreateStorage();
        // Should silently succeed
        var act = () => storage.TryDelete(null);
        act.Should().NotThrow();
    }

    [Fact]
    public void TryDelete_WhitespacePath_DoesNotThrow()
    {
        var storage = CreateStorage();
        var act = () => storage.TryDelete("   ");
        act.Should().NotThrow();
    }

    [Fact]
    public void TryDelete_NonExistentFile_DoesNotThrow()
    {
        var storage = CreateStorage();
        var act = () => storage.TryDelete("banner-builder/99/nonexistent.jpg");
        act.Should().NotThrow();
    }

    // ── EnsureUserDirectory ───────────────────────────────────────────────────

    [Fact]
    public void EnsureUserDirectory_CreatesAndReturnsPath()
    {
        var tmpRoot = Path.Combine(Path.GetTempPath(), "bannershop-test-" + Guid.NewGuid().ToString("N"));
        var storage = CreateStorage(localRoot: tmpRoot);

        try
        {
            var dir = storage.EnsureUserDirectory(userId: 42);

            dir.Should().EndWith("42");
            Directory.Exists(dir).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tmpRoot))
                Directory.Delete(tmpRoot, recursive: true);
        }
    }

    [Fact]
    public void EnsureUserDirectory_Anonymous_UsesZeroSubdir()
    {
        var tmpRoot = Path.Combine(Path.GetTempPath(), "bannershop-test-" + Guid.NewGuid().ToString("N"));
        var storage = CreateStorage(localRoot: tmpRoot);

        try
        {
            var dir = storage.EnsureUserDirectory(userId: null);

            dir.Should().EndWith("0");
        }
        finally
        {
            if (Directory.Exists(tmpRoot))
                Directory.Delete(tmpRoot, recursive: true);
        }
    }
}
