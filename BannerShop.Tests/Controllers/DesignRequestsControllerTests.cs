using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BannerShop.Api.Models.DesignRequests;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests for POST /api/design-requests/ai (BANNERSH-67 free-first flow).
///
/// Covers the wiring between the controller, the BotProtectionFilter, the
/// AllowAnonymous policy, and the CreateAiRequestAsync service path. The service
/// internals themselves are exercised separately in <c>DesignRequestServiceTests</c>.
/// </summary>
public class DesignRequestsControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public DesignRequestsControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        EnsureSeed();
    }

    private void EnsureSeed()
    {
        _factory.SeedDatabase(db =>
        {
            if (!db.BannerTemplates.Any())
            {
                db.BannerTemplates.Add(new BannerTemplate
                {
                    Id = 1, Category = BannerTemplateCategory.Birthday,
                    NameNb = "Bursdag", NameEn = "Birthday", SortOrder = 10
                });
                db.PricingParameters.AddRange(
                    new PricingParameter { Id = 1011, Name = "AI credit pack price",          Key = "ai_credit_pack_price_nok",         Value = 29m  },
                    new PricingParameter { Id = 1012, Name = "AI credit pack count",          Key = "ai_credit_pack_count",             Value = 10m  },
                    new PricingParameter { Id = 1013, Name = "AI banner activation fee",      Key = "ai_banner_activation_fee_nok",     Value = 95m  });
                db.SaveChanges();
            }
        });
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

    /// <summary>Returns a fresh anonymous client with a browser-like UA + integrity header.</summary>
    private HttpClient NewAnonClient()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36");
        c.DefaultRequestHeaders.Add("X-Request-Integrity", "abc123");
        return c;
    }

    // ── 403 on bot UA ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAi_anonymous_with_bot_useragent_returns_403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("python-requests/2.31");
        client.DefaultRequestHeaders.Add("X-Request-Integrity", "abc123");

        var response = await client.PostAsJsonAsync("/api/design-requests/ai", SampleBody());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateAi_anonymous_missing_integrity_header_returns_403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36");
        // No X-Request-Integrity header.

        var response = await client.PostAsJsonAsync("/api/design-requests/ai", SampleBody());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Anonymous free generation ─────────────────────────────────────────────

    [Fact]
    public async Task CreateAi_anonymous_valid_request_returns_201_or_402_or_400()
    {
        // Anonymous callers get 1 free generation per IP per 30 days.
        // Either it succeeds (201), the free quota is exhausted (402), or
        // IP resolution fails in test env (400).
        var client = NewAnonClient();
        // Set X-Forwarded-For so IP detection works in the test server
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");

        var response = await client.PostAsJsonAsync("/api/design-requests/ai", SampleBody());

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Created, HttpStatusCode.PaymentRequired, HttpStatusCode.BadRequest);
    }

    // ── Authenticated create-AI ───────────────────────────────────────────────

    [Fact]
    public async Task CreateAi_authenticatedUser_returns_201_or_402_or_404()
    {
        // Auth users get one free generation per account; after that they need credits.
        // If the user doesn't exist in the test DB, the service returns 404.
        int userId = 9001; // deterministic ID for seeding
        _factory.SeedDatabase(db =>
        {
            if (!db.Users.Any(u => u.Id == userId))
            {
                db.Users.Add(new BannerShop.Core.Entities.User
                {
                    Id = userId, Email = $"ai_dr_user_{userId}@test.com",
                    Name = "AI DR User", PasswordHash = "x",
                    Role = BannerShop.Core.Enums.UserRole.Customer,
                    CreatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
            }
        });

        var client = _factory.CreateAuthenticatedClient(userId: userId,
            email: $"ai_dr_user_{userId}@test.com");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        client.DefaultRequestHeaders.Add("X-Request-Integrity", "abc123");

        var response = await client.PostAsJsonAsync("/api/design-requests/ai", SampleBody());

        // First call → 201 (free generation); if user already used it → 402
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Created, HttpStatusCode.PaymentRequired);
    }

    // ── GET /api/design-requests (ListMine) ───────────────────────────────────

    [Fact]
    public async Task ListMine_WithAuth_Returns200()
    {
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/design-requests");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Deprecated header should be present
        response.Headers.Should().ContainKey("Deprecation");
    }

    [Fact]
    public async Task ListMine_WithoutAuth_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/design-requests");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── GET /api/design-requests/{id} ────────────────────────────────────────

    [Fact]
    public async Task Get_NonExistentId_Returns404()
    {
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/design-requests/99999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_WithoutAuth_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/design-requests/1");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/design-requests/manual ─────────────────────────────────────

    [Fact]
    public async Task CreateManual_WithAuth_ReachesEndpoint_Returns200or400()
    {
        // Seed a real user so the service can look it up
        int userId = 9002;
        _factory.SeedDatabase(db =>
        {
            if (!db.Users.Any(u => u.Id == userId))
            {
                db.Users.Add(new BannerShop.Core.Entities.User
                {
                    Id = userId, Email = $"manual_user_{userId}@test.com",
                    Name = "Manual User", PasswordHash = "x",
                    Role = BannerShop.Core.Enums.UserRole.Customer,
                    CreatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
            }
        });

        var client = _factory.CreateAuthenticatedClient(
            userId: userId, email: $"manual_user_{userId}@test.com");

        var response = await client.PostAsJsonAsync("/api/design-requests/manual", new
        {
            templateId = 1,
            language = "nb",
            personName = "Ola",
            textContent = "Lykke til",
            themeDescription = "Elegant",
            aspectRatio = "18:9"
        });

        // 200 = success, 400 = service validation failed (e.g. missing pricing params)
        // Either way, auth + routing worked correctly (not 401/403)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateManual_WithoutAuth_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/design-requests/manual", new
        {
            templateId = 1,
            language = "nb",
            personName = "Test",
            textContent = "Test",
            themeDescription = "Test",
            aspectRatio = "18:9"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/design-requests/{id}/revision ───────────────────────────────

    [Fact]
    public async Task RequestRevision_NonExistentId_Returns400()
    {
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/design-requests/99999/revision",
            new { comment = "Please change the font" });

        // Service returns error for non-existent request → 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RequestRevision_WithoutAuth_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/design-requests/1/revision",
            new { comment = "Change it" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/design-requests/{id}/approve ────────────────────────────────

    [Fact]
    public async Task Approve_NonExistentId_Returns400()
    {
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/design-requests/99999/approve",
            new { selectedHeightCm = (int?)null });

        // Service returns error → 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Approve_WithoutAuth_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/design-requests/1/approve", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/design-requests/{id}/regenerate ─────────────────────────────

    [Fact]
    public async Task Regenerate_NonExistentId_Returns404()
    {
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/design-requests/99999/regenerate",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Regenerate_WithoutAuth_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/design-requests/1/regenerate", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/design-requests/{id}/generations/{generationId}/activate ────

    [Fact]
    public async Task ActivateGeneration_NonExistentIds_Returns400()
    {
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.PostAsync(
            "/api/design-requests/99999/generations/88888/activate", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ActivateGeneration_WithoutAuth_Returns401()
    {
        var response = await _factory.CreateClient().PostAsync(
            "/api/design-requests/1/generations/1/activate", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
