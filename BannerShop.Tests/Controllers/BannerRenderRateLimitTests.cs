using System.Net;
using System.Net.Http.Json;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests verifying that the two remaining anonymous image-rendering
/// endpoints are rate-limited per IP:
///
///   PUT /api/banner-builder/{id}/rotate   — re-renders the preview JPEG from the
///     original artwork on every call with no result cache, and anonymous designs
///     are reachable by any caller (BannerBuilderController.UserCanAccess returns
///     true when UserId is null).
///   GET /api/banner-preview/generate      — renders an 800 px eyelet-overlay JPEG
///     on a cache miss; a scripted caller can walk designId × eyelet combinations.
///
/// Both requests below target a design id that does not exist, so they 404 without
/// touching the image pipeline — the rate limiter runs as endpoint middleware, ahead
/// of the action, so the permit is still consumed and the 429 is deterministic.
///
/// Each factory overrides the rate-limiter configuration to 1 permit with
/// AutoReplenishment disabled (never a short WindowSeconds — that races with real
/// wall-clock delays under a loaded/parallel full-suite run; see AuthRateLimitTestFactory).
/// </summary>
public class BannerRotateRateLimitTests : IClassFixture<BannerRotateRateLimitTestFactory>
{
    private readonly BannerRotateRateLimitTestFactory _factory;

    public BannerRotateRateLimitTests(BannerRotateRateLimitTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Rotate_ExceedsRateLimit_Returns429()
    {
        var client = _factory.CreateClient();
        var body = new { degrees = 90 };

        var r1 = await client.PutAsJsonAsync("/api/banner-builder/999999/rotate", body);
        r1.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "the first request must not be rate-limited");

        var r2 = await client.PutAsJsonAsync("/api/banner-builder/999999/rotate", body);
        r2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}

public class BannerPreviewGenerateRateLimitTests : IClassFixture<BannerPreviewRateLimitTestFactory>
{
    private readonly BannerPreviewRateLimitTestFactory _factory;

    public BannerPreviewGenerateRateLimitTests(BannerPreviewRateLimitTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Generate_ExceedsRateLimit_Returns429()
    {
        var client = _factory.CreateClient();

        var r1 = await client.GetAsync("/api/banner-preview/generate?designId=999999&eyelet=None");
        r1.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "the first request must not be rate-limited");

        var r2 = await client.GetAsync("/api/banner-preview/generate?designId=999999&eyelet=None");
        r2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Serve_IsNotRateLimited()
    {
        // Only the rendering route carries a limiter — serving a cached JPEG by its
        // content-addressed GUID is a plain file read and must stay unthrottled so a
        // single page full of previews cannot lock a visitor out.
        var client = _factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            var r = await client.GetAsync($"/api/banner-preview/{new string('a', 32)}");
            r.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }
    }
}

/// <summary>
/// WebApplicationFactory variant that caps the banner-rotate limit at 1 permit
/// so a 429 response can be asserted deterministically.
/// </summary>
public class BannerRotateRateLimitTestFactory : TestWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:BannerRotate:PermitLimit"]       = "1",
                ["RateLimiting:BannerRotate:AutoReplenishment"] = "false",
            });
        });
    }
}

/// <summary>
/// WebApplicationFactory variant that caps the banner-preview limit at 1 permit
/// so a 429 response can be asserted deterministically.
/// </summary>
public class BannerPreviewRateLimitTestFactory : TestWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:BannerPreview:PermitLimit"]       = "1",
                ["RateLimiting:BannerPreview:AutoReplenishment"] = "false",
            });
        });
    }
}
