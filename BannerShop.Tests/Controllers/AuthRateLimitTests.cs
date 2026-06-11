using System.Net;
using System.Net.Http.Json;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests that verify the rate-limiting middleware returns HTTP 429
/// after the per-IP permit limit is exceeded on auth endpoints.
///
/// Uses <see cref="AuthRateLimitTestFactory"/> which overrides the rate-limiter
/// configuration to 1 request / 2 s — fast and deterministic without any sleeps.
/// </summary>
public class AuthRateLimitTests : IClassFixture<AuthRateLimitTestFactory>
{
    private readonly AuthRateLimitTestFactory _factory;

    public AuthRateLimitTests(AuthRateLimitTestFactory factory)
    {
        _factory = factory;
    }

    // ── POST /api/auth/login ──────────────────────────────────────────────────

    [Fact]
    public async Task Login_ExceedsRateLimit_Returns429()
    {
        // Use a fresh client per test to get an isolated connection (new IP bucket).
        // In TestServer all requests appear from 127.0.0.1, so each test class
        // shares the same IP bucket — use a brand-new factory client each time.
        var client = _factory.CreateClient();
        var body = new { email = "attacker@example.com", password = "wrong" };

        // First request — allowed (returns 401 since credentials are bad, not 429)
        var r1 = await client.PostAsJsonAsync("/api/auth/login", body);
        r1.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "the first request must not be rate-limited");

        // Second request — should be rejected by the rate limiter (limit = 1 / 2 s)
        var r2 = await client.PostAsJsonAsync("/api/auth/login", body);
        r2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // ── POST /api/auth/register ───────────────────────────────────────────────

    [Fact]
    public async Task Register_ExceedsRateLimit_Returns429()
    {
        var client = _factory.CreateClient();
        // Use a unique email so the first call succeeds (200 OK or 400 on dup is fine —
        // we only care that it is NOT 429).
        var body = new { email = $"rl_{Guid.NewGuid():N}@test.com", password = "Secure123!", name = "RL" };

        var r1 = await client.PostAsJsonAsync("/api/auth/register", body);
        r1.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);

        // Second call (same IP, limit = 1 / 2 s) → 429
        var r2 = await client.PostAsJsonAsync("/api/auth/register", body);
        r2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // ── POST /api/auth/refresh ────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_ExceedsRateLimit_Returns429()
    {
        var client = _factory.CreateClient();
        var body = new { refreshToken = "fake-token" };

        // First request — allowed (returns 401 for invalid token, not 429)
        var r1 = await client.PostAsJsonAsync("/api/auth/refresh", body);
        r1.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);

        // Second request — should be rejected by the rate limiter (limit = 1 / 2 s)
        var r2 = await client.PostAsJsonAsync("/api/auth/refresh", body);
        r2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // ── POST /api/auth/change-password ────────────────────────────────────────

    [Fact]
    public async Task ChangePassword_ExceedsRateLimit_Returns429()
    {
        // change-password is [Authorize] — provide a valid Bearer token so
        // authentication doesn't reject the request before the rate limiter fires.
        // Rate limiting runs before authentication in the middleware pipeline, so
        // even authenticated users are subject to the per-IP window.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", TestWebApplicationFactory.MakeJwt(userId: 42));

        var body = new { currentPassword = "OldPass!", newPassword = "NewPass!" };

        // First request — allowed (returns 400 for wrong password or 204 if seed matched;
        // either way it must not be 429)
        var r1 = await client.PostAsJsonAsync("/api/auth/change-password", body);
        r1.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);

        // Second request — should be rejected (limit = 1 / 2 s)
        var r2 = await client.PostAsJsonAsync("/api/auth/change-password", body);
        r2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}

/// <summary>
/// WebApplicationFactory variant that overrides rate-limiting configuration to
/// 1 request per 2 seconds on all auth policies so 429 responses can be asserted
/// in tests without real 60-second windows.
/// </summary>
public class AuthRateLimitTestFactory : TestWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Apply all base overrides first (InMemory DB, JWT, mock Stripe, …)
        base.ConfigureWebHost(builder);

        // Override rate-limit permits to 1 / 2 s — the base factory sets 1000
        // which would never trigger a 429.  Since ConfigureAppConfiguration
        // callbacks are applied in registration order and later providers win on
        // duplicate keys, this override takes precedence.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Login:PermitLimit"]            = "1",
                ["RateLimiting:Login:WindowSeconds"]          = "2",
                ["RateLimiting:Register:PermitLimit"]         = "1",
                ["RateLimiting:Register:WindowSeconds"]       = "2",
                ["RateLimiting:Refresh:PermitLimit"]          = "1",
                ["RateLimiting:Refresh:WindowSeconds"]        = "2",
                ["RateLimiting:ChangePassword:PermitLimit"]   = "1",
                ["RateLimiting:ChangePassword:WindowSeconds"] = "2",
            });
        });
    }
}
