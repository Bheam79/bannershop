using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Core.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Unit tests for <see cref="BannerPreviewService"/>. The class carries a class-level
/// <c>[ExcludeFromCodeCoverage]</c> ("tested via integration"), but had zero direct test
/// coverage despite owning real logic: deterministic cache-key hashing, cache reuse
/// (skip regeneration once a file exists), the resize-to-FixedMaxPx rule, eyelet-overlay
/// drawing, and the GUID-format guard in <see cref="BannerPreviewService.ResolvePreviewPath"/>.
/// Exercised against a real temp directory + real PNG/JPEG files since image I/O is the
/// entire point of the class (same approach as <c>LocalDiskFileStoreTests</c>).
/// </summary>
public class BannerPreviewServiceTests : IDisposable
{
    private readonly string _root;
    private readonly BannerFileStorage _storage;
    private readonly BannerPreviewService _service;

    public BannerPreviewServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bannershop-preview-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var options = Options.Create(new FileStorageOptions { LocalRoot = _root, PublicBaseUrl = "/files" });
        _storage = new BannerFileStorage(options);
        _service = new BannerPreviewService(_storage, NullLogger<BannerPreviewService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string CreateSourceImage(string relativeName, int width, int height, Rgba32? fill = null)
    {
        var abs = Path.Combine(_root, relativeName);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        using var img = new Image<Rgba32>(width, height, fill ?? new Rgba32(255, 255, 255, 255));
        img.SaveAsPng(abs);
        return relativeName;
    }

    // -- ResolvePreviewPath: GUID format guard --

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    [InlineData("ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")] // uppercase not allowed
    [InlineData("not-32-hex-chars-but-has-dashes-x")]
    [InlineData("../../etc/passwd")]
    public void ResolvePreviewPath_RejectsInvalidGuidFormat(string? guid)
    {
        _service.ResolvePreviewPath(guid).Should().BeNull();
    }

    [Fact]
    public void ResolvePreviewPath_ValidFormatButNoCacheFile_ReturnsNull()
    {
        var fakeGuid = new string('a', 32);
        _service.ResolvePreviewPath(fakeGuid).Should().BeNull();
    }

    // -- GetPreviewGuidAsync: missing source --

    [Fact]
    public async Task GetPreviewGuidAsync_MissingSourceFile_ReturnsGuidButNoCacheCreated()
    {
        var guid = await _service.GetPreviewGuidAsync("banner-builder/0/missing.png", 300, 150, EyeletOption.None);

        guid.Should().HaveLength(32);
        _service.ResolvePreviewPath(guid).Should().BeNull();
    }

    // -- GetPreviewGuidAsync: deterministic hashing --

    [Fact]
    public async Task GetPreviewGuidAsync_SameInputs_ReturnsSameGuid()
    {
        var rel = CreateSourceImage("banner-builder/0/a.png", 100, 50);

        var guid1 = await _service.GetPreviewGuidAsync(rel, 300, 150, EyeletOption.FourCorners);
        var guid2 = await _service.GetPreviewGuidAsync(rel, 300, 150, EyeletOption.FourCorners);

        guid2.Should().Be(guid1);
    }

    [Theory]
    [MemberData(nameof(DifferingInputVariants))]
    public async Task GetPreviewGuidAsync_DifferentInputs_ReturnsDifferentGuid(
        int widthCm, int heightCm, EyeletOption eyelet)
    {
        var rel = CreateSourceImage("banner-builder/0/b.png", 100, 50);

        var baseline = await _service.GetPreviewGuidAsync(rel, 300, 150, EyeletOption.None);
        var variant = await _service.GetPreviewGuidAsync(rel, widthCm, heightCm, eyelet);

        variant.Should().NotBe(baseline);
    }

    public static IEnumerable<object[]> DifferingInputVariants()
    {
        yield return new object[] { 301, 150, EyeletOption.None };
        yield return new object[] { 300, 151, EyeletOption.None };
        yield return new object[] { 300, 150, EyeletOption.FourCorners };
    }

    // -- GetPreviewGuidAsync: cache creation + reuse --

    [Fact]
    public async Task GetPreviewGuidAsync_CreatesCacheFile_ResolvableAfterwards()
    {
        var rel = CreateSourceImage("banner-builder/0/c.png", 100, 50);

        var guid = await _service.GetPreviewGuidAsync(rel, 300, 150, EyeletOption.None);

        var path = _service.ResolvePreviewPath(guid);
        path.Should().NotBeNull();
        File.Exists(path!).Should().BeTrue();
        path.Should().EndWith($"{guid}.jpg");
    }

    [Fact]
    public async Task GetPreviewGuidAsync_ReusesCache_EvenIfSourceLaterDeleted()
    {
        var abs = Path.Combine(_root, "banner-builder/0/d.png");
        var rel = CreateSourceImage("banner-builder/0/d.png", 100, 50);

        var guid = await _service.GetPreviewGuidAsync(rel, 300, 150, EyeletOption.None);
        var cachePath = _service.ResolvePreviewPath(guid);
        cachePath.Should().NotBeNull();

        File.Delete(abs); // prove regeneration is NOT attempted on the cache-hit path

        var guidAgain = await _service.GetPreviewGuidAsync(rel, 300, 150, EyeletOption.None);

        guidAgain.Should().Be(guid);
        _service.ResolvePreviewPath(guidAgain).Should().Be(cachePath);
    }

    // -- Resize-to-FixedMaxPx rule --

    [Fact]
    public async Task GetPreviewGuidAsync_LargerThanFixedMax_IsDownscaledPreservingAspect()
    {
        var rel = CreateSourceImage("banner-builder/0/big.png", 1600, 800);

        var guid = await _service.GetPreviewGuidAsync(rel, 300, 150, EyeletOption.None);
        var path = _service.ResolvePreviewPath(guid)!;

        using var output = await Image.LoadAsync<Rgba32>(path);
        output.Width.Should().Be(800);
        output.Height.Should().Be(400);
    }

    [Fact]
    public async Task GetPreviewGuidAsync_SmallerThanFixedMax_KeepsOriginalSize()
    {
        var rel = CreateSourceImage("banner-builder/0/small.png", 100, 50);

        var guid = await _service.GetPreviewGuidAsync(rel, 300, 150, EyeletOption.None);
        var path = _service.ResolvePreviewPath(guid)!;

        using var output = await Image.LoadAsync<Rgba32>(path);
        output.Width.Should().Be(100);
        output.Height.Should().Be(50);
    }

    // -- Eyelet overlay drawing --

    [Fact]
    public async Task GetPreviewGuidAsync_FourCornersEyelet_DrawsRedNearCorners()
    {
        var rel = CreateSourceImage("banner-builder/0/eyelet.png", 200, 100, new Rgba32(255, 255, 255, 255));

        var guid = await _service.GetPreviewGuidAsync(rel, 300, 150, EyeletOption.FourCorners);
        var path = _service.ResolvePreviewPath(guid)!;

        using var output = await Image.LoadAsync<Rgba32>(path);
        var topLeft = output[15, 15];

        // JPEG is lossy, so assert "clearly reddish and not white" rather than an exact RGB match.
        topLeft.R.Should().BeGreaterThan(150);
        topLeft.G.Should().BeLessThan(150);
        topLeft.B.Should().BeLessThan(150);
    }

    [Fact]
    public async Task GetPreviewGuidAsync_NoneEyelet_LeavesCornersUntouched()
    {
        var rel = CreateSourceImage("banner-builder/0/noeyelet.png", 200, 100, new Rgba32(255, 255, 255, 255));

        var guid = await _service.GetPreviewGuidAsync(rel, 300, 150, EyeletOption.None);
        var path = _service.ResolvePreviewPath(guid)!;

        using var output = await Image.LoadAsync<Rgba32>(path);
        var topLeft = output[15, 15];

        topLeft.R.Should().BeGreaterThan(240);
        topLeft.G.Should().BeGreaterThan(240);
        topLeft.B.Should().BeGreaterThan(240);
    }

    [Fact]
    public async Task GetPreviewGuidAsync_EyeletRequestedButZeroDimensions_SkipsDrawing()
    {
        var rel = CreateSourceImage("banner-builder/0/zerodim.png", 200, 100, new Rgba32(255, 255, 255, 255));

        var guid = await _service.GetPreviewGuidAsync(rel, 0, 0, EyeletOption.FourCorners);
        var path = _service.ResolvePreviewPath(guid)!;

        using var output = await Image.LoadAsync<Rgba32>(path);
        var topLeft = output[15, 15];

        topLeft.R.Should().BeGreaterThan(240);
        topLeft.G.Should().BeGreaterThan(240);
        topLeft.B.Should().BeGreaterThan(240);
    }
}
