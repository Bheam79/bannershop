using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests for ShippingController.
/// Uses MockShippingService (injected by TestWebApplicationFactory).
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
            bannerSizeId = 1,
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
    public async Task Calculate_NonExistentSize_Returns404()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            bannerSizeId = 99999,
            qty = 1,
            postalCode = "0001",
            city = "Oslo",
            packingMode = "Folded"
        };

        var response = await client.PostAsJsonAsync("/api/shipping/calculate", req);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Calculate_CustomWidthSizeWithoutCustomWidthCm_Returns400()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            bannerSizeId = 6,       // Size 6 is custom-width
            qty = 1,
            postalCode = "0001",
            city = "Oslo",
            packingMode = "Folded"
            // no customWidthCm
        };

        var response = await client.PostAsJsonAsync("/api/shipping/calculate", req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Calculate_CustomWidthSizeWithCustomWidthCm_Returns200()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            bannerSizeId = 6,
            customWidthCm = 250,
            qty = 1,
            postalCode = "0001",
            city = "Oslo",
            packingMode = "Rolled"
        };

        var response = await client.PostAsJsonAsync("/api/shipping/calculate", req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── POST /api/shipping/parcel-preview ─────────────────────────────────────

    [Fact]
    public async Task ParcelPreview_ValidRequest_Returns200WithDimensions()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            bannerSizeId = 1,
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
            bannerSizeId = 1,
            qty = 2,
            packingMode = "Rolled"
        };

        var response = await client.PostAsJsonAsync("/api/shipping/parcel-preview", req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ParcelPreview_NonExistentSize_Returns404()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            bannerSizeId = 99999,
            qty = 1,
            packingMode = "Folded"
        };

        var response = await client.PostAsJsonAsync("/api/shipping/parcel-preview", req);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ParcelPreview_CustomWidthSizeWithoutCustomWidthCm_Returns400()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            bannerSizeId = 6,       // custom-width size
            qty = 1,
            packingMode = "Folded"
            // no customWidthCm
        };

        var response = await client.PostAsJsonAsync("/api/shipping/parcel-preview", req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ParcelPreview_CustomWidthSizeWithCustomWidthCm_Returns200()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            bannerSizeId = 6,
            customWidthCm = 300,
            qty = 1,
            packingMode = "Folded"
        };

        var response = await client.PostAsJsonAsync("/api/shipping/parcel-preview", req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── POST /api/shipping/calculate — error paths ────────────────────────────

    [Fact]
    public async Task Calculate_InvalidModelState_Returns400()
    {
        var client = _factory.CreateClient();
        // PostalCode is too short (min 4 chars)
        var req = new
        {
            postalCode = "AB",   // too short — violates [StringLength(20, MinimumLength = 4)]
            bannerSizeId = 1,
            qty = 1
        };

        var response = await client.PostAsJsonAsync("/api/shipping/calculate", req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Calculate_NonExistentBannerSize_Returns404()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            postalCode = "0150",
            city = "Oslo",
            bannerSizeId = 99999,
            qty = 1
        };

        var response = await client.PostAsJsonAsync("/api/shipping/calculate", req);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Calculate_CustomWidthSizeExistingBanner_MissingCustomWidth_Returns400()
    {
        var client = _factory.CreateClient();
        // size id 6 is custom-width
        var req = new
        {
            postalCode = "0150",
            city = "Oslo",
            bannerSizeId = 6,
            qty = 1
            // no customWidthCm
        };

        var response = await client.PostAsJsonAsync("/api/shipping/calculate", req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── POST /api/shipping/parcel-preview — model state failure ───────────────

    [Fact]
    public async Task ParcelPreview_InvalidModelState_Returns400()
    {
        var client = _factory.CreateClient();
        // qty = 0 violates [Range(1, 1000)]
        var req = new
        {
            bannerSizeId = 1,
            qty = 0,           // invalid: must be >= 1
            packingMode = "Folded"
        };

        var response = await client.PostAsJsonAsync("/api/shipping/parcel-preview", req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
