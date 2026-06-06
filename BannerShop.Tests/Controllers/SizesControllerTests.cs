using System.Net;
using System.Text.Json;
using BannerShop.Core.Entities;
using BannerShop.Infrastructure.Data;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests for the public SizesController.
/// Seeds catalog data per-test to keep tests isolated.
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
    }

    [Fact]
    public async Task GetSizes_WithCustomWidthCm_Returns200()
    {
        EnsureCatalogSeeded();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/sizes?customWidthCm=200");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GET /api/sizes/{id}/price ─────────────────────────────────────────────

    [Fact]
    public async Task GetPrice_StandardSize_Returns200WithPrice()
    {
        EnsureCatalogSeeded();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/sizes/1/price");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("priceNok");
    }

    [Fact]
    public async Task GetPrice_FixedPriceSize_Returns699()
    {
        EnsureCatalogSeeded();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/sizes/7/price");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(body, _json);
        doc.GetProperty("priceNok").GetDecimal().Should().Be(699m);
    }

    [Fact]
    public async Task GetPrice_CustomWidthSizeWithoutCustomWidthCm_Returns400()
    {
        EnsureCatalogSeeded();
        var client = _factory.CreateClient();

        // Size 6 is custom-width → requires ?customWidthCm
        var response = await client.GetAsync("/api/sizes/6/price");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetPrice_CustomWidthSizeWithCustomWidthCm_Returns200()
    {
        EnsureCatalogSeeded();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/sizes/6/price?customWidthCm=200");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPrice_UnknownSizeId_Returns404()
    {
        EnsureCatalogSeeded();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/sizes/99999/price");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
