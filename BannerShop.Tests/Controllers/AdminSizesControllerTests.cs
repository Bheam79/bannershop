using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests for AdminSizesController. BANNERSH-255 — pricing rules are
/// (min/max width × min/max height + pricing formula).
/// </summary>
public class AdminSizesControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public AdminSizesControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        EnsureSeed();
    }

    private void EnsureSeed()
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

    private HttpClient AdminClient() =>
        _factory.CreateAuthenticatedClient(userId: 99, email: "admin@test.com", name: "Admin", role: UserRole.Admin);

    private HttpClient CustomerClient() =>
        _factory.CreateAuthenticatedClient(userId: 50, email: "cust@test.com", name: "Customer", role: UserRole.Customer);

    private async Task<int> GetFirstMaterialIdAsync()
    {
        var resp = await AdminClient().GetAsync("/api/admin/materials");
        var body = await resp.Content.ReadAsStringAsync();
        var materials = JsonSerializer.Deserialize<JsonElement[]>(body, _json)!;
        return materials[0].GetProperty("id").GetInt32();
    }

    // ── GET /api/admin/sizes ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_WithAdminToken_Returns200WithSizes()
    {
        var response = await AdminClient().GetAsync("/api/admin/sizes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("minWidthCm");
        body.Should().Contain("calculatedPrice");
    }

    [Fact]
    public async Task GetAll_WithoutAuth_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/admin/sizes");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAll_WithCustomerToken_Returns403()
    {
        var response = await CustomerClient().GetAsync("/api/admin/sizes");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── POST /api/admin/sizes ─────────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidRequest_Returns201WithCreatedSize()
    {
        var materialId = await GetFirstMaterialIdAsync();

        var response = await AdminClient().PostAsJsonAsync("/api/admin/sizes", new
        {
            name = "TestSize-" + Guid.NewGuid().ToString("N")[..8],
            isActive = true,
            materialId,
            sortOrder = 99,
            minWidthCm = 1,
            maxWidthCm = 500,
            minHeightCm = 1,
            maxHeightCm = 200,
            pricingHeightCm = 154,
            pricingMultiplier = 1,
            fixedPrice = (decimal?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("minWidthCm");
        body.Should().Contain("pricingHeightCm");
    }

    [Fact]
    public async Task Create_InvalidMaterialId_Returns400()
    {
        var response = await AdminClient().PostAsJsonAsync("/api/admin/sizes", new
        {
            name = "GhostSize",
            isActive = true,
            materialId = 99999,
            sortOrder = 1,
            minWidthCm = 1,
            maxWidthCm = 500,
            minHeightCm = 1,
            maxHeightCm = 150,
            pricingHeightCm = 150,
            pricingMultiplier = 1,
            fixedPrice = (decimal?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── PUT /api/admin/sizes/{id} ─────────────────────────────────────────────

    [Fact]
    public async Task Update_ExistingSize_Returns200WithUpdatedData()
    {
        var materialId = await GetFirstMaterialIdAsync();

        var createResp = await AdminClient().PostAsJsonAsync("/api/admin/sizes", new
        {
            name = "ToUpdate-" + Guid.NewGuid().ToString("N")[..8],
            isActive = true,
            materialId,
            sortOrder = 98,
            minWidthCm = 1,
            maxWidthCm = 500,
            minHeightCm = 1,
            maxHeightCm = 200,
            pricingHeightCm = 154,
            pricingMultiplier = 1,
            fixedPrice = (decimal?)null
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = JsonSerializer.Deserialize<JsonElement>(
            await createResp.Content.ReadAsStringAsync(), _json);
        var id = created.GetProperty("id").GetInt32();

        var response = await AdminClient().PutAsJsonAsync($"/api/admin/sizes/{id}", new
        {
            name = "UpdatedSize",
            isActive = false,
            materialId,
            sortOrder = 97,
            minWidthCm = 1,
            maxWidthCm = 500,
            minHeightCm = 1,
            maxHeightCm = 300,
            pricingHeightCm = 180,
            pricingMultiplier = 2,
            fixedPrice = (decimal?)500m
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("UpdatedSize");
    }

    [Fact]
    public async Task Update_NonExistentSize_Returns404()
    {
        var materialId = await GetFirstMaterialIdAsync();

        var response = await AdminClient().PutAsJsonAsync("/api/admin/sizes/99999", new
        {
            name = "Ghost",
            isActive = true,
            materialId,
            sortOrder = 1,
            minWidthCm = 1,
            maxWidthCm = 500,
            minHeightCm = 1,
            maxHeightCm = 154,
            pricingHeightCm = 154,
            pricingMultiplier = 1,
            fixedPrice = (decimal?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DELETE /api/admin/sizes/{id} ──────────────────────────────────────────

    [Fact]
    public async Task Delete_SizeNotInUse_Returns204()
    {
        var materialId = await GetFirstMaterialIdAsync();

        var createResp = await AdminClient().PostAsJsonAsync("/api/admin/sizes", new
        {
            name = "DeleteMe-" + Guid.NewGuid().ToString("N")[..8],
            isActive = false,
            materialId,
            sortOrder = 100,
            minWidthCm = 1,
            maxWidthCm = 999,
            minHeightCm = 1,
            maxHeightCm = 888,
            pricingHeightCm = 154,
            pricingMultiplier = 1,
            fixedPrice = (decimal?)null
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = JsonSerializer.Deserialize<JsonElement>(
            await createResp.Content.ReadAsStringAsync(), _json);
        var id = created.GetProperty("id").GetInt32();

        var response = await AdminClient().DeleteAsync($"/api/admin/sizes/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_NonExistentSize_Returns404()
    {
        var response = await AdminClient().DeleteAsync("/api/admin/sizes/99999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
