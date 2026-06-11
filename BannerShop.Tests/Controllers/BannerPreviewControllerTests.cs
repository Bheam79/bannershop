using System.Net;
using BannerShop.Core.Entities;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests for BannerPreviewController.
/// The controller is AllowAnonymous; tests cover the not-found paths
/// (no real image files available in the test server environment).
/// </summary>
public class BannerPreviewControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BannerPreviewControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── GET /api/banner-preview/generate ─────────────────────────────────────

    [Fact]
    public async Task Generate_NonExistentDesignId_Returns404()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/banner-preview/generate?designId=99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Generate_WithoutAuth_IsAllowedAndReturns404ForMissingDesign()
    {
        // Endpoint is AllowAnonymous — no 401 expected
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/banner-preview/generate?designId=0");

        // 0 will not be found in DB → 404 (not 401)
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Generate_DefaultEyeletOption_IsAccepted()
    {
        var client = _factory.CreateClient();

        // eyelet param not required — default is None
        var response = await client.GetAsync("/api/banner-preview/generate?designId=1");

        // Still 404 because design doesn't exist, but the route accepted the request
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── GET /api/banner-preview/{guid} ─────────────────────────────────────────

    [Fact]
    public async Task Serve_InvalidGuid_Returns404()
    {
        var client = _factory.CreateClient();

        // Non-existent GUID → ResolvePreviewPath returns null → 404
        var response = await client.GetAsync("/api/banner-preview/aaaabbbbccccddddeeeeffffaabbccdd");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Serve_ShortGuid_Returns404()
    {
        var client = _factory.CreateClient();

        // Less than 32 hex chars — the service will reject it
        var response = await client.GetAsync("/api/banner-preview/abc123");

        // Either 404 from ResolvePreviewPath or 400 from model validation
        ((int)response.StatusCode).Should().BeOneOf(404, 400, 405);
    }

    // ── Generate with BannerDesign that has no storage path ────────────────────

    [Fact]
    public async Task Generate_DesignWithNoStoragePath_Returns404WithMessage()
    {
        // Seed a BannerDesign with empty StoragePath AND empty PreviewStoragePath
        // so the "No source image available" branch is hit.
        int designId = 87654;
        _factory.SeedDatabase(db =>
        {
            if (!db.BannerDesigns.Any(d => d.Id == designId))
            {
                db.BannerDesigns.Add(new BannerDesign
                {
                    Id          = designId,
                    UserId      = null,
                    StoragePath = string.Empty,       // ← triggers null/empty check
                    PreviewStoragePath = null,
                    OriginalFileName   = "test.jpg",
                    ContentType        = "image/jpeg",
                    RotationDegrees    = 0,
                    ComputedWidthCm    = 0,
                    SelectedHeightCm   = 0,
                    CreatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
            }
        });

        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/banner-preview/generate?designId={designId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("source image");
    }
}
