using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests verifying that admin endpoints enforce role-based access control.
/// </summary>
public class AdminControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public AdminControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Auth guards ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/admin/orders")]
    [InlineData("/api/admin/materials")]
    [InlineData("/api/admin/sizes")]
    [InlineData("/api/admin/pricing-parameters")]
    public async Task AdminEndpoint_WithoutToken_Returns401(string path)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/admin/orders")]
    [InlineData("/api/admin/materials")]
    [InlineData("/api/admin/sizes")]
    [InlineData("/api/admin/pricing-parameters")]
    public async Task AdminEndpoint_WithCustomerToken_Returns403(string path)
    {
        // Customer role ≠ Admin → 403 Forbidden
        var client = _factory.CreateAuthenticatedClient(
            userId: 1, email: "customer@test.com", name: "Customer", role: UserRole.Customer);

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminOrders_WithAdminToken_Returns200()
    {
        var client = _factory.CreateAuthenticatedClient(
            userId: 99, email: "admin@test.com", name: "Admin", role: UserRole.Admin);

        var response = await client.GetAsync("/api/admin/orders");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminMaterials_WithAdminToken_Returns200()
    {
        // Ensure at least one material exists
        _factory.SeedDatabase(db =>
        {
            if (!db.Materials.Any())
                DbHelper.SeedCatalog(db);
        });

        var client = _factory.CreateAuthenticatedClient(role: UserRole.Admin);

        var response = await client.GetAsync("/api/admin/materials");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminSizes_WithAdminToken_Returns200()
    {
        var client = _factory.CreateAuthenticatedClient(role: UserRole.Admin);

        var response = await client.GetAsync("/api/admin/sizes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminPricing_WithAdminToken_Returns200()
    {
        var client = _factory.CreateAuthenticatedClient(role: UserRole.Admin);

        var response = await client.GetAsync("/api/admin/pricing-parameters");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Admin orders filter ───────────────────────────────────────────────────

    [Fact]
    public async Task AdminOrders_WithPageParams_Returns200PagedResult()
    {
        var client = _factory.CreateAuthenticatedClient(role: UserRole.Admin);

        var response = await client.GetAsync("/api/admin/orders?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("items");
        body.Should().Contain("totalCount");
    }
}
