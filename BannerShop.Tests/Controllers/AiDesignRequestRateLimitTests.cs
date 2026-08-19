using System.Net;
using System.Net.Http.Json;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration test verifying that the anonymous POST /api/design-requests/ai endpoint
/// is rate-limited per IP. Each call can trigger a real OpenAI image-generation spend
/// (BANNERSH-67); before this test the only anonymous-abuse guard was
/// <see cref="BannerShop.Api.Services.AiCredits.BotProtectionFilter"/>, which only checks
/// User-Agent/header shape and is trivially satisfied by a scripted caller.
///
/// Uses the corresponding TestFactory which overrides the
/// rate-limiter configuration to 1 permit with AutoReplenishment disabled
/// (never a short WindowSeconds — that races with real wall-clock delays
/// under a loaded/parallel full-suite run; see AuthRateLimitTestFactory).
/// </summary>
public class AiDesignRequestRateLimitTests : IClassFixture<AiDesignRequestRateLimitTestFactory>
{
    private readonly AiDesignRequestRateLimitTestFactory _factory;

    public AiDesignRequestRateLimitTests(AiDesignRequestRateLimitTestFactory factory)
    {
        _factory = factory;
    }

    private static object SampleBody() => new
    {
        templateId = 1,
        language = "nb",
        personName = "Ola",
        textContent = "Gratulerer",
        themeDescription = "tropisk",
        aspectRatio = "16:9"
    };

    private HttpClient NewAnonClient()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36");
        c.DefaultRequestHeaders.Add("X-Request-Integrity", "abc123");
        return c;
    }

    [Fact]
    public async Task CreateAi_ExceedsRateLimit_Returns429()
    {
        var client = NewAnonClient();

        var r1 = await client.PostAsJsonAsync("/api/design-requests/ai", SampleBody());
        r1.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "the first request must not be rate-limited");

        var r2 = await client.PostAsJsonAsync("/api/design-requests/ai", SampleBody());
        r2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}

/// <summary>
/// WebApplicationFactory variant that overrides the ai-design-request rate limit
/// to 1 request per 2 seconds so a 429 response can be asserted deterministically.
/// </summary>
public class AiDesignRequestRateLimitTestFactory : TestWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:AiDesignRequest:PermitLimit"]       = "1",
                ["RateLimiting:AiDesignRequest:AutoReplenishment"] = "false",
            });
        });
    }
}
