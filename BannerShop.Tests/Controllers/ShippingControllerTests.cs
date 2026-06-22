using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests for ShippingController. BANNERSH-255 — requests carry
/// (materialId + widthCm + heightCm) directly; there's no banner-size-id lookup.
/// </summary>
public class ShippingControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public ShippingControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        EnsureCatalogSeeded();
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

    // ── POST /api/shipping/calculate ──────────────────────────────────────────

    [Fact]
    public async Task Calculate_ValidRequest_Returns200WithQuote()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            materialId = 1,
            widthCm = 300,
            heightCm = 150,
            qty = 1,
            postalCode = "0001",
            city = "Oslo",
            packingMode = "Folded"
        };

        var response = await client.PostAsJsonAsync("/api/shipping/calculate", req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("standard");
        body.Should().Contain("parcel");
    }

    [Fact]
    public async Task Calculate_NonExistentMaterial_Returns404()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            materialId = 99999,
            widthCm = 300,
            heightCm = 150,
            qty = 1,
            postalCode = "0001",
            city = "Oslo",
            packingMode = "Folded"
        };

        var response = await client.PostAsJsonAsync("/api/shipping/calculate", req);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST /api/shipping/parcel-preview ─────────────────────────────────────

    [Fact]
    public async Task ParcelPreview_ValidRequest_Returns200WithDimensions()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            materialId = 1,
            widthCm = 300,
            heightCm = 150,
            qty = 1,
            packingMode = "Folded"
        };

        var response = await client.PostAsJsonAsync("/api/shipping/parcel-preview", req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("lengthCm");
        body.Should().Contain("widthCm");
        body.Should().Contain("weightKg");
    }

    [Fact]
    public async Task ParcelPreview_RolledMode_Returns200()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            materialId = 1,
            widthCm = 300,
            heightCm = 150,
            qty = 2,
            packingMode = "Rolled"
        };

        var response = await client.PostAsJsonAsync("/api/shipping/parcel-preview", req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ParcelPreview_NonExistentMaterial_Returns404()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            materialId = 99999,
            widthCm = 300,
            heightCm = 150,
            qty = 1,
            packingMode = "Folded"
        };

        var response = await client.PostAsJsonAsync("/api/shipping/parcel-preview", req);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Calculate_InvalidModelState_Returns400()
    {
        var client = _factory.CreateClient();
        // PostalCode is too short (min 4 chars)
        var req = new
        {
            postalCode = "AB",
            materialId = 1,
            widthCm = 300,
            heightCm = 150,
            qty = 1
        };

        var response = await client.PostAsJsonAsync("/api/shipping/calculate", req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ParcelPreview_InvalidModelState_Returns400()
    {
        var client = _factory.CreateClient();
        // qty = 0 violates [Range(1, 1000)]
        var req = new
        {
            materialId = 1,
            widthCm = 300,
            heightCm = 150,
            qty = 0,
            packingMode = "Folded"
        };

        var response = await client.PostAsJsonAsync("/api/shipping/parcel-preview", req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
