using System.Text;
using BannerShop.Api.Services.BannerBuilder;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Unit tests for <see cref="LocalDiskFileStore"/>. The class carries a class-level
/// <c>[ExcludeFromCodeCoverage]</c> ("tested via integration"), but <see cref="LocalDiskFileStore.NormalizePath"/>
/// is the actual directory-traversal guard shared by every caller of <c>IFileStore.SaveAsync</c>
/// and had zero direct test coverage. The instance methods are exercised against a real temp
/// directory rather than mocked, since the whole point of the class is local filesystem I/O.
/// </summary>
public class LocalDiskFileStoreTests : IDisposable
{
    private readonly string _root;
    private readonly LocalDiskFileStore _store;

    public LocalDiskFileStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bannershop-filestore-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var options = Options.Create(new FileStorageOptions { LocalRoot = _root, PublicBaseUrl = "/files" });
        _store = new LocalDiskFileStore(options, NullLogger<LocalDiskFileStore>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // ── NormalizePath ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("banner-builder/42", "abc.jpg", "banner-builder/42/abc.jpg")]
    [InlineData("banner-builder/42/", "abc.jpg", "banner-builder/42/abc.jpg")]
    [InlineData("design-requests/7", "abc.png", "design-requests/7/abc.png")]
    public void NormalizePath_JoinsAndTrimsSlashes(string subPath, string fileName, string expected)
    {
        LocalDiskFileStore.NormalizePath(subPath, fileName).Should().Be(expected);
    }

    [Theory]
    [InlineData("../etc", "passwd")]
    [InlineData("banner-builder/../../etc", "passwd")]
    [InlineData("banner-builder", "../../etc/passwd")]
    [InlineData("banner-builder", "..")]
    public void NormalizePath_RejectsTraversalSegments(string subPath, string fileName)
    {
        var act = () => LocalDiskFileStore.NormalizePath(subPath, fileName);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("/etc/passwd", "abc.jpg")]
    [InlineData("banner-builder", "/etc/passwd")]
    public void NormalizePath_RejectsRootedComponents(string subPath, string fileName)
    {
        var act = () => LocalDiskFileStore.NormalizePath(subPath, fileName);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("", "abc.jpg")]
    [InlineData("   ", "abc.jpg")]
    [InlineData("banner-builder", "")]
    [InlineData("banner-builder", "   ")]
    public void NormalizePath_RejectsEmptyComponents(string subPath, string fileName)
    {
        var act = () => LocalDiskFileStore.NormalizePath(subPath, fileName);
        act.Should().Throw<ArgumentException>();
    }

    // ── NewFileName ──────────────────────────────────────────────────────────

    [Fact]
    public void NewFileName_ProducesGuidWithLowercasedExtension()
    {
        var name = LocalDiskFileStore.NewFileName(".JPG");
        name.Should().MatchRegex(@"^[0-9a-f]{32}\.jpg$");
    }

    [Fact]
    public void NewFileName_StripsLeadingDotAndIsUnique()
    {
        var a = LocalDiskFileStore.NewFileName("png");
        var b = LocalDiskFileStore.NewFileName("png");
        a.Should().EndWith(".png");
        a.Should().NotBe(b);
    }

    // ── ToAbsolute / GetPublicUrl ────────────────────────────────────────────

    [Fact]
    public void ToAbsolute_CombinesRootWithNormalizedRelativePath()
    {
        var abs = _store.ToAbsolute("banner-builder/42/abc.jpg");
        abs.Should().Be(Path.Combine(_root, "banner-builder", "42", "abc.jpg"));
    }

    [Fact]
    public void GetPublicUrl_JoinsBaseUrlAndStoragePathWithoutDoubleSlash()
    {
        _store.GetPublicUrl("banner-builder/42/abc.jpg").Should().Be("/files/banner-builder/42/abc.jpg");
        _store.GetPublicUrl("/banner-builder/42/abc.jpg").Should().Be("/files/banner-builder/42/abc.jpg");
    }

    // ── SaveAsync / OpenReadAsync / DeleteAsync round trip ──────────────────

    [Fact]
    public async Task SaveAsync_WritesFileUnderRootAndReturnsMetadata()
    {
        var bytes = Encoding.UTF8.GetBytes("hello banner");
        using var content = new MemoryStream(bytes);

        var result = await _store.SaveAsync(content, "text/plain", "design-requests/7", "note.txt");

        result.StoragePath.Should().Be("design-requests/7/note.txt");
        result.PublicUrl.Should().Be("/files/design-requests/7/note.txt");
        result.SizeBytes.Should().Be(bytes.Length);
        File.Exists(Path.Combine(_root, "design-requests", "7", "note.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_CreatesMissingIntermediateDirectories()
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("x"));

        await _store.SaveAsync(content, "text/plain", "a/b/c", "leaf.txt");

        Directory.Exists(Path.Combine(_root, "a", "b", "c")).Should().BeTrue();
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsStoredBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("round trip content");
        using (var content = new MemoryStream(bytes))
            await _store.SaveAsync(content, "text/plain", "design-requests/7", "note.txt");

        await using var read = await _store.OpenReadAsync("design-requests/7/note.txt");
        using var ms = new MemoryStream();
        await read.CopyToAsync(ms);

        ms.ToArray().Should().Equal(bytes);
    }

    [Fact]
    public async Task OpenReadAsync_MissingFile_ThrowsFileNotFoundException()
    {
        var act = async () => await _store.OpenReadAsync("design-requests/999/missing.txt");
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_ExistingFile_DeletesAndReturnsTrue()
    {
        using (var content = new MemoryStream(Encoding.UTF8.GetBytes("x")))
            await _store.SaveAsync(content, "text/plain", "design-requests/7", "note.txt");

        var deleted = await _store.DeleteAsync("design-requests/7/note.txt");

        deleted.Should().BeTrue();
        File.Exists(Path.Combine(_root, "design-requests", "7", "note.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_MissingFile_ReturnsFalseWithoutThrowing()
    {
        var deleted = await _store.DeleteAsync("design-requests/999/missing.txt");
        deleted.Should().BeFalse();
    }
}
