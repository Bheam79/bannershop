using System.Net;
using System.Text.Json;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests for MaterialsController (GET /api/materials).
/// </summary>
public class MaterialsControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public MaterialsControllerTests(TestWebApplicationFactory factory)
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

    [Fact]
    public async Task GetMaterials_Returns200WithList()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/materials");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var items = JsonSerializer.Deserialize<JsonElement[]>(body, _json)!;
        items.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetMaterials_ResponseContainsExpectedFields()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/materials");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("name");
        body.Should().Contain("widthCm");
        body.Should().Contain("weightGsm");
        body.Should().Contain("pricePerSqm");
    }

    [Fact]
    public async Task GetMaterials_NoAuthRequired_Returns200()
    {
        // Public endpoint — no Bearer token
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/materials");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
