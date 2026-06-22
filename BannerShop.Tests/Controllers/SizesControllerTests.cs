using System.Net;
using System.Text.Json;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests for the public SizesController. BANNERSH-255 — sizes are
/// now range-based pricing rules and pricing is queried with explicit width/height.
/// </summary>
public class SizesControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public SizesControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private void EnsureCatalogSeeded()
    {
        _factory.SeedDatabase(db =>
        {
            if (!db.Materials.Any())
            {
                DbHelper.SeedPricingParameters(db);
                DbHelper.SeedCatalog(db);
            }
        });
    }

    // ── GET /api/sizes ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSizes_Returns200WithList()
    {
        EnsureCatalogSeeded();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/sizes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var sizes = JsonSerializer.Deserialize<JsonElement[]>(body, _json)!;
        sizes.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetSizes_ResponseContainsCalculatedPrice()
    {
        EnsureCatalogSeeded();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/sizes");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("calculatedPrice");
        body.Should().Contain("pricingHeightCm");
        body.Should().Contain("pricingMultiplier");
    }

    // ── GET /api/sizes/price?widthCm=&heightCm= ───────────────────────────────

    [Fact]
    public async Task GetPrice_ValidDims_Returns200WithCheapestMatch()
    {
        EnsureCatalogSeeded();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/sizes/price?widthCm=300&heightCm=150");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("priceNok");
        body.Should().Contain("sizeId");
    }

    [Fact]
    public async Task GetPrice_FixedSizeDimensions_PicksFixedPriceRule()
    {
        EnsureCatalogSeeded();
        var client = _factory.CreateClient();

        // Seeded rule id=7 covers 300×180 on mat1 (400g) with FixedPrice = 699. (BANNERSH-259)
        var response = await client.GetAsync("/api/sizes/price?widthCm=300&heightCm=180&materialId=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(body, _json);
        doc.GetProperty("priceNok").GetDecimal().Should().Be(699m);
        doc.GetProperty("sizeId").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task GetPrice_MissingDims_Returns400()
    {
        EnsureCatalogSeeded();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/sizes/price?widthCm=0&heightCm=150");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetPrice_NoMatchingRule_Returns404()
    {
        EnsureCatalogSeeded();
        var client = _factory.CreateClient();

        // No seeded rule covers 9999×9999.
        var response = await client.GetAsync("/api/sizes/price?widthCm=9999&heightCm=9999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Availability flag (BANNERSH-259) ─────────────────────────────────────
    //
    // The picker composable (useBannerPricing) reads `availableFrom` from the
    // GET /api/sizes response to determine `isComingSoon`. This test guards the
    // seed so the 680g material (IDs 1–3, 8) is available NOW and the 400g
    // material (IDs 4–7) is future. A mismatch here would cause the picker to
    // show "Kommer snart" on actually-available rules.

    [Fact]
    public async Task GetSizes_SeededCatalog_680gMaterialHasNullAvailableFrom()
    {
        EnsureCatalogSeeded();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/sizes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var sizes = JsonSerializer.Deserialize<JsonElement[]>(body, _json)!;

        // Rule id=1 (680g×154) should have material with no availableFrom (available now).
        var rule1 = sizes.FirstOrDefault(s => s.GetProperty("id").GetInt32() == 1);
        rule1.ValueKind.Should().NotBe(JsonValueKind.Undefined, "rule id=1 should exist in seeded catalog");
        var mat = rule1.GetProperty("material");
        mat.GetProperty("weightGsm").GetInt32().Should().Be(680);
        // availableFrom is null → serialized as absent or JsonNull
        mat.TryGetProperty("availableFrom", out var af).Should().BeTrue();
        af.ValueKind.Should().Be(JsonValueKind.Null, "680g material must be available now (null availableFrom)");
    }

    [Fact]
    public async Task GetSizes_SeededCatalog_400gMaterialHasFutureAvailableFrom()
    {
        EnsureCatalogSeeded();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/sizes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var sizes = JsonSerializer.Deserialize<JsonElement[]>(body, _json)!;

        // Rule id=4 (400g×180) should have material with a future availableFrom.
        var rule4 = sizes.FirstOrDefault(s => s.GetProperty("id").GetInt32() == 4);
        rule4.ValueKind.Should().NotBe(JsonValueKind.Undefined, "rule id=4 should exist in seeded catalog");
        var mat = rule4.GetProperty("material");
        mat.GetProperty("weightGsm").GetInt32().Should().Be(400);
        mat.TryGetProperty("availableFrom", out var af).Should().BeTrue();
        af.ValueKind.Should().Be(JsonValueKind.String, "400g material must have a future availableFrom date");
        var availableFrom = af.GetDateTime();
        availableFrom.Should().BeAfter(DateTime.UtcNow, "400g material is not yet in production");
    }

    // ── GET /api/sizes/eyelet-price ───────────────────────────────────────────

    [Fact]
    public async Task GetEyeletPrice_Returns200WithPrice()
    {
        EnsureCatalogSeeded();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/sizes/eyelet-price");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("pricePerEyeletNok");
    }
}
