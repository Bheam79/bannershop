using System.Net;
using System.Net.Http.Json;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration test verifying that the anonymous /api/analytics/track endpoint
/// is rate-limited per IP (BANNERSH-285 added it with no throttling at all).
///
/// Uses the corresponding TestFactory which overrides the
/// rate-limiter configuration to 1 permit with AutoReplenishment disabled
/// (never a short WindowSeconds — that races with real wall-clock delays
/// under a loaded/parallel full-suite run; see AuthRateLimitTestFactory).
/// </summary>
public class AnalyticsRateLimitTests : IClassFixture<AnalyticsRateLimitTestFactory>
{
    private readonly AnalyticsRateLimitTestFactory _factory;

    public AnalyticsRateLimitTests(AnalyticsRateLimitTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Track_ExceedsRateLimit_Returns429()
    {
        var client = _factory.CreateClient();
        var body = new
        {
            sessionId = Guid.NewGuid().ToString("N"),
            isNewSession = true,
            path = "/",
            referrer = (string?)null,
        };

        var r1 = await client.PostAsJsonAsync("/api/analytics/track", body);
        r1.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "the first request must not be rate-limited");

        var r2 = await client.PostAsJsonAsync("/api/analytics/track", body);
        r2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}

/// <summary>
/// WebApplicationFactory variant that overrides the analytics-track rate limit
/// to 1 request per 2 seconds so a 429 response can be asserted deterministically.
/// </summary>
public class AnalyticsRateLimitTestFactory : TestWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:AnalyticsTrack:PermitLimit"]       = "1",
                ["RateLimiting:AnalyticsTrack:AutoReplenishment"] = "false",
            });
        });
    }
}
