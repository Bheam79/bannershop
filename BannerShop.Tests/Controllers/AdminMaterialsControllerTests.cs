using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests for AdminMaterialsController (CRUD for materials).
/// </summary>
public class AdminMaterialsControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public AdminMaterialsControllerTests(TestWebApplicationFactory factory)
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

    // ── GET /api/admin/materials ──────────────────────────────────────────────

    [Fact]
    public async Task GetAll_WithAdminToken_Returns200WithMaterials()
    {
        var response = await AdminClient().GetAsync("/api/admin/materials");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("name");
        body.Should().Contain("pricePerSqm");
    }

    [Fact]
    public async Task GetAll_WithoutAuth_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/admin/materials");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAll_WithCustomerToken_Returns403()
    {
        var response = await CustomerClient().GetAsync("/api/admin/materials");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── POST /api/admin/materials ─────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidRequest_Returns201WithCreatedMaterial()
    {
        var response = await AdminClient().PostAsJsonAsync("/api/admin/materials", new
        {
            name = "TestMat-" + Guid.NewGuid().ToString("N")[..8],
            widthCm = (int?)160,
            maxBannerWidthCm = (int?)0,   // 0 → defaults to widthCm
            weightGsm = 400,
            pricePerSqm = 250m,
            availableFrom = (DateTime?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("250");
    }

    [Fact]
    public async Task Create_WithMaxBannerWidth_StoresMaxBannerWidth()
    {
        var response = await AdminClient().PostAsJsonAsync("/api/admin/materials", new
        {
            name = "TestMat-" + Guid.NewGuid().ToString("N")[..8],
            widthCm = (int?)180,
            maxBannerWidthCm = (int?)120,   // custom max width
            weightGsm = 680,
            pricePerSqm = 320m,
            availableFrom = (DateTime?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("maxBannerWidthCm");
    }

    // ── PUT /api/admin/materials/{id} ─────────────────────────────────────────

    [Fact]
    public async Task Update_ExistingMaterial_Returns200WithUpdatedData()
    {
        // First create a material to update
        var createResp = await AdminClient().PostAsJsonAsync("/api/admin/materials", new
        {
            name = "UpdateMe-" + Guid.NewGuid().ToString("N")[..8],
            widthCm = (int?)160,
            maxBannerWidthCm = (int?)0,
            weightGsm = 400,
            pricePerSqm = 200m,
            availableFrom = (DateTime?)null
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = JsonSerializer.Deserialize<JsonElement>(
            await createResp.Content.ReadAsStringAsync(), _json);
        var id = created.GetProperty("id").GetInt32();

        var response = await AdminClient().PutAsJsonAsync($"/api/admin/materials/{id}", new
        {
            name = "UpdatedMaterial",
            widthCm = (int?)200,
            maxBannerWidthCm = (int?)0,
            weightGsm = 500,
            pricePerSqm = 299m,
            availableFrom = (DateTime?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("UpdatedMaterial");
        body.Should().Contain("299");
    }

    [Fact]
    public async Task Update_NonExistentMaterial_Returns404()
    {
        var response = await AdminClient().PutAsJsonAsync("/api/admin/materials/99999", new
        {
            name = "Ghost",
            widthCm = (int?)160,
            maxBannerWidthCm = (int?)0,
            weightGsm = 400,
            pricePerSqm = 200m,
            availableFrom = (DateTime?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DELETE /api/admin/materials/{id} ──────────────────────────────────────

    [Fact]
    public async Task Delete_MaterialNotInUse_Returns204()
    {
        // Create a material with no banner sizes attached
        var createResp = await AdminClient().PostAsJsonAsync("/api/admin/materials", new
        {
            name = "DeleteMe-" + Guid.NewGuid().ToString("N")[..8],
            widthCm = (int?)100,
            maxBannerWidthCm = (int?)0,
            weightGsm = 300,
            pricePerSqm = 100m,
            availableFrom = (DateTime?)null
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = JsonSerializer.Deserialize<JsonElement>(
            await createResp.Content.ReadAsStringAsync(), _json);
        var id = created.GetProperty("id").GetInt32();

        var response = await AdminClient().DeleteAsync($"/api/admin/materials/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_NonExistentMaterial_Returns404()
    {
        var response = await AdminClient().DeleteAsync("/api/admin/materials/99999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_MaterialInUseByBannerSize_Returns409()
    {
        // Materials seeded in TestData (id=1 or id=2) are linked to banner sizes
        // The conflict response is 409
        var allResp = await AdminClient().GetAsync("/api/admin/materials");
        var allBody = await allResp.Content.ReadAsStringAsync();
        var materials = JsonSerializer.Deserialize<JsonElement[]>(allBody, _json)!;
        if (materials.Length == 0) return; // no seeded materials

        var firstId = materials[0].GetProperty("id").GetInt32();
        var response = await AdminClient().DeleteAsync($"/api/admin/materials/{firstId}");

        // Seeded materials are in use by sizes → 409 Conflict
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
