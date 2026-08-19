using System.Net;
using BannerShop.Core.Entities;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Covers the BannerPreviewController.Generate/Serve success paths that
/// BannerPreviewControllerTests can't reach (it only has not-found designs, so
/// no real source file is ever read). Uses a dedicated factory pointing
/// FileStorage:LocalRoot at a real temp directory so the real
/// BannerPreviewService/ImageProcessingService pipeline runs end to end.
/// </summary>
public class BannerPreviewGenerateSuccessTests : IClassFixture<BannerPreviewGenerateTestFactory>
{
    private readonly BannerPreviewGenerateTestFactory _factory;

    public BannerPreviewGenerateSuccessTests(BannerPreviewGenerateTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Generate_RealSourceFile_WithoutComputedDimensions_ReturnsOkAndFallsBackToDefaultCm()
    {
        // ComputedWidthCm=0 / SelectedHeightCm=0 exercises the "false" branch of both
        // fallback ternaries (falls back to 100x150) and the StoragePath-as-source branch.
        int designId = 90001;
        var relPath = _factory.WriteSourceImage(designId, 400, 300);
        _factory.SeedDatabase(db =>
        {
            db.BannerDesigns.Add(new BannerDesign
            {
                Id = designId,
                UserId = null,
                StoragePath = relPath,
                PreviewStoragePath = null,
                OriginalFileName = "source.jpg",
                ContentType = "image/jpeg",
                RotationDegrees = 0,
                ComputedWidthCm = 0,
                SelectedHeightCm = 0,
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        });

        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/banner-preview/generate?designId={designId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadJsonAsync<GenerateResponse>();
        body.Should().NotBeNull();
        body!.guid.Should().MatchRegex("^[0-9a-f]{32}$");
        body.previewUrl.Should().Contain(body.guid);
    }

    [Fact]
    public async Task Generate_PrefersPreviewStoragePath_WhenSet_AndUsesComputedDimensions()
    {
        // PreviewStoragePath non-blank exercises the "true" branch of the sourcePath
        // ternary (preview preferred over full-res original); ComputedWidthCm/SelectedHeightCm
        // > 0 exercises the "true" branch of both fallback ternaries.
        int designId = 90002;
        var fullResPath = _factory.WriteSourceImage(designId, 4000, 3000, "full.jpg");
        var previewPath = _factory.WriteSourceImage(designId, 400, 300, "preview.jpg");
        _factory.SeedDatabase(db =>
        {
            db.BannerDesigns.Add(new BannerDesign
            {
                Id = designId,
                UserId = null,
                StoragePath = fullResPath,
                PreviewStoragePath = previewPath,
                OriginalFileName = "source.jpg",
                ContentType = "image/jpeg",
                RotationDegrees = 0,
                ComputedWidthCm = 267,
                SelectedHeightCm = 150,
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        });

        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/banner-preview/generate?designId={designId}&eyelet=FourCorners");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadJsonAsync<GenerateResponse>();
        body!.guid.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public async Task Serve_AfterGenerate_ReturnsJpegWithCacheHeaders_ThenNotModifiedForMatchingETag()
    {
        int designId = 90003;
        var relPath = _factory.WriteSourceImage(designId, 500, 400);
        _factory.SeedDatabase(db =>
        {
            db.BannerDesigns.Add(new BannerDesign
            {
                Id = designId,
                UserId = null,
                StoragePath = relPath,
                PreviewStoragePath = null,
                OriginalFileName = "source.jpg",
                ContentType = "image/jpeg",
                RotationDegrees = 0,
                ComputedWidthCm = 180,
                SelectedHeightCm = 150,
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        });

        var client = _factory.CreateClient();
        var generateResponse = await client.GetAsync($"/api/banner-preview/generate?designId={designId}");
        var generated = await generateResponse.ReadJsonAsync<GenerateResponse>();

        var serveResponse = await client.GetAsync($"/api/banner-preview/{generated!.guid}");
        serveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        serveResponse.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        serveResponse.Headers.ETag!.Tag.Should().Be($"\"{generated.guid}\"");
        serveResponse.Headers.CacheControl!.Public.Should().BeTrue();
        var bytes = await serveResponse.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(0);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/banner-preview/{generated.guid}");
        request.Headers.IfNoneMatch.ParseAdd($"\"{generated.guid}\"");
        var notModifiedResponse = await client.SendAsync(request);
        notModifiedResponse.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    private sealed record GenerateResponse(string previewUrl, string guid);
}

/// <summary>
/// Points FileStorage:LocalRoot at a real temp directory (instead of the default
/// /workspace/uploads) so tests can write real source images without polluting
/// the dev environment's upload folder, and cleans it up on disposal.
/// </summary>
public class BannerPreviewGenerateTestFactory : TestWebApplicationFactory
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "bannershop-preview-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        Directory.CreateDirectory(_root);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:LocalRoot"] = _root,
            });
        });
    }

    /// <summary>Writes a real JPEG source image and returns its BannerDesign.StoragePath-relative path.</summary>
    public string WriteSourceImage(int userId, int width, int height, string fileName = "source.jpg")
    {
        var dir = Path.Combine(_root, "banner-builder", userId.ToString());
        Directory.CreateDirectory(dir);
        var abs = Path.Combine(dir, fileName);
        using var img = new Image<Rgba32>(width, height, Color.SteelBlue);
        img.SaveAsJpeg(abs);
        return $"banner-builder/{userId}/{fileName}";
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }
    }
}
