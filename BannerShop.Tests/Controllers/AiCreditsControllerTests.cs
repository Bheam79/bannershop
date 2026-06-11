using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests for AiCreditsController (BANNERSH-69, BANNERSH-137).
/// Covers auth guard and happy-path behaviour of the credit-pack endpoints.
/// </summary>
public class AiCreditsControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public AiCreditsControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        EnsurePricingParamsSeeded();
    }

    private void EnsurePricingParamsSeeded()
    {
        _factory.SeedDatabase(db =>
        {
            if (!db.PricingParameters.Any())
            {
                DbHelper.SeedPricingParameters(db);
            }
        });
    }

    // ── Public pack info (BANNERSH-71, BANNERSH-137) ─────────────────────────

    [Fact]
    public async Task GetCreditPackInfo_WithoutAuth_Returns200WithBothTiers()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/ai-credits/packs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), _json);

        // Small tier
        doc.GetProperty("small").GetProperty("priceNok").GetDecimal().Should().Be(29m,
            "ai_credit_pack_price_nok is seeded to 29 kr");
        doc.GetProperty("small").GetProperty("creditCount").GetInt32().Should().Be(5,
            "ai_credit_pack_count is seeded to 5 (BANNERSH-137)");

        // Large tier
        doc.GetProperty("large").GetProperty("priceNok").GetDecimal().Should().Be(95m,
            "ai_credit_pack_large_price_nok is seeded to 95 kr");
        doc.GetProperty("large").GetProperty("creditCount").GetInt32().Should().Be(20,
            "ai_credit_pack_large_count is seeded to 20");
    }

    // ── Auth guard ───────────────────────────────────────────────────────────

    [Fact]
    public async Task BuyCreditPack_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/ai-credits/packs/buy", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/ai-credits/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_WithAuth_Returns200WithBalanceInfo()
    {
        var client = RegisterAndGetAuthenticatedClient();

        var response = await client.GetAsync("/api/ai-credits/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), _json);
        doc.TryGetProperty("creditsRemaining", out _).Should().BeTrue();
        doc.TryGetProperty("hasUsedFreeGeneration", out _).Should().BeTrue();
    }

    // ── Happy path — small pack ───────────────────────────────────────────────

    [Fact]
    public async Task BuyCreditPack_SmallPack_Returns200WithCorrectPricing()
    {
        var client = RegisterAndGetAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/ai-credits/packs/buy", new { pack = "small" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(body, _json);

        doc.GetProperty("clientSecret").GetString().Should().NotBeNullOrEmpty(
            "MockStripePaymentService must return a deterministic fake client secret");
        doc.GetProperty("creditCount").GetInt32().Should().Be(5,
            "ai_credit_pack_count is seeded to 5 (BANNERSH-137)");
        doc.GetProperty("priceNok").GetDecimal().Should().Be(29m,
            "ai_credit_pack_price_nok is seeded to 29 kr");
        doc.GetProperty("pack").GetString().Should().Be("small");
    }

    [Fact]
    public async Task BuyCreditPack_DefaultPack_Returns200WithSmallTier()
    {
        var client = RegisterAndGetAuthenticatedClient();

        // POST with no body — should default to small pack for backward compat.
        var response = await client.PostAsync("/api/ai-credits/packs/buy", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(body, _json);

        doc.GetProperty("creditCount").GetInt32().Should().Be(5,
            "default pack is 'small' which has 5 credits");
        doc.GetProperty("priceNok").GetDecimal().Should().Be(29m);
    }

    // ── Happy path — large pack ───────────────────────────────────────────────

    [Fact]
    public async Task BuyCreditPack_LargePack_Returns200WithLargeTierPricing()
    {
        var client = RegisterAndGetAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/ai-credits/packs/buy", new { pack = "large" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(body, _json);

        doc.GetProperty("clientSecret").GetString().Should().NotBeNullOrEmpty();
        doc.GetProperty("creditCount").GetInt32().Should().Be(20,
            "ai_credit_pack_large_count is seeded to 20");
        doc.GetProperty("priceNok").GetDecimal().Should().Be(95m,
            "ai_credit_pack_large_price_nok is seeded to 95 kr");
        doc.GetProperty("pack").GetString().Should().Be("large");
    }

    [Fact]
    public async Task BuyCreditPack_InvalidPack_Returns400()
    {
        var client = RegisterAndGetAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/ai-credits/packs/buy", new { pack = "mega" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Pricing reflected from DB ─────────────────────────────────────────────

    [Fact]
    public async Task BuyCreditPack_PricingFromDb_ReflectsSeededValues()
    {
        // Verify the endpoint reads from DB, not hard-coded defaults.
        _factory.SeedDatabase(db =>
        {
            var smallPrice = db.PricingParameters.FirstOrDefault(p => p.Key == "ai_credit_pack_price_nok");
            var smallCount = db.PricingParameters.FirstOrDefault(p => p.Key == "ai_credit_pack_count");
            var largePrice = db.PricingParameters.FirstOrDefault(p => p.Key == "ai_credit_pack_large_price_nok");
            var largeCount = db.PricingParameters.FirstOrDefault(p => p.Key == "ai_credit_pack_large_count");

            smallPrice?.Value.Should().Be(29m);
            smallCount?.Value.Should().Be(5m);
            largePrice?.Value.Should().Be(95m);
            largeCount?.Value.Should().Be(20m);
        });

        var client = RegisterAndGetAuthenticatedClient();

        // Small pack
        var smallResp = await client.PostAsJsonAsync("/api/ai-credits/packs/buy", new { pack = "small" });
        smallResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var smallDoc = JsonSerializer.Deserialize<JsonElement>(
            await smallResp.Content.ReadAsStringAsync(), _json);
        smallDoc.GetProperty("priceNok").GetDecimal().Should().Be(29m);
        smallDoc.GetProperty("creditCount").GetInt32().Should().Be(5);

        // Large pack
        var largeResp = await client.PostAsJsonAsync("/api/ai-credits/packs/buy", new { pack = "large" });
        largeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var largeDoc = JsonSerializer.Deserialize<JsonElement>(
            await largeResp.Content.ReadAsStringAsync(), _json);
        largeDoc.GetProperty("priceNok").GetDecimal().Should().Be(95m);
        largeDoc.GetProperty("creditCount").GetInt32().Should().Be(20);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private HttpClient RegisterAndGetAuthenticatedClient()
    {
        var email = $"credits_{Guid.NewGuid():N}@test.com";
        var client = _factory.CreateClient();

        var regResp = client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Pass123!", name = "Credits User" }).GetAwaiter().GetResult();
        var body = regResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var json = JsonSerializer.Deserialize<JsonElement>(body, _json);
        var token = json.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
