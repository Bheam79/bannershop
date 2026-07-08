using System.Net;
using System.Net.Http.Headers;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration test verifying that the anonymous /api/banner-builder/upload endpoint
/// is rate-limited per IP (it accepts up to 75 MB and runs CPU-bound PDF/image
/// processing per request, with no throttling before this test was added).
///
/// Uses <see cref="BannerUploadRateLimitTestFactory"/> which overrides the
/// rate-limiter configuration to 1 request / 2 s.
/// </summary>
public class BannerUploadRateLimitTests : IClassFixture<BannerUploadRateLimitTestFactory>
{
    private readonly BannerUploadRateLimitTestFactory _factory;

    public BannerUploadRateLimitTests(BannerUploadRateLimitTestFactory factory)
    {
        _factory = factory;
    }

    private static byte[] JpegBytes() =>
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01];

    private static MultipartFormDataContent MakeUpload(string name, byte[] content, string mime)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(fileContent, "file", name);
        return form;
    }

    [Fact]
    public async Task Upload_ExceedsRateLimit_Returns429()
    {
        var client = _factory.CreateClient();

        var r1 = await client.PostAsync("/api/banner-builder/upload", MakeUpload("a.jpg", JpegBytes(), "image/jpeg"));
        r1.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "the first request must not be rate-limited");

        var r2 = await client.PostAsync("/api/banner-builder/upload", MakeUpload("b.jpg", JpegBytes(), "image/jpeg"));
        r2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}

/// <summary>
/// WebApplicationFactory variant that overrides the banner-upload rate limit
/// to 1 request per 2 seconds so a 429 response can be asserted deterministically.
/// Extends BannerBuilderTestFactory (not the base factory) so the mocked
/// IImageProcessingService + temp FileStorage root are still in place.
/// </summary>
public class BannerUploadRateLimitTestFactory : BannerBuilderTestFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:BannerUpload:PermitLimit"]   = "1",
                ["RateLimiting:BannerUpload:WindowSeconds"] = "2",
            });
        });
    }
}
