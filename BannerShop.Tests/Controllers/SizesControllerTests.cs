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

        // Seeded rule id=7 covers 300×180 with FixedPrice = 699.
        var response = await client.GetAsync("/api/sizes/price?widthCm=300&heightCm=180&materialId=2");

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
