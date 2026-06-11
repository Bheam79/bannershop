using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests for AdminPricingController (pricing parameters CRUD).
/// </summary>
public class AdminPricingControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public AdminPricingControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        EnsureSeed();
    }

    private void EnsureSeed()
    {
        _factory.SeedDatabase(db =>
        {
            if (!db.PricingParameters.Any())
                DbHelper.SeedPricingParameters(db);
        });
    }

    private HttpClient AdminClient() =>
        _factory.CreateAuthenticatedClient(userId: 99, email: "admin@test.com", name: "Admin", role: UserRole.Admin);

    private HttpClient CustomerClient() =>
        _factory.CreateAuthenticatedClient(userId: 50, email: "cust@test.com", name: "Customer", role: UserRole.Customer);

    // ── GET /api/admin/pricing-parameters ────────────────────────────────────

    [Fact]
    public async Task GetAll_WithAdminToken_Returns200WithParameters()
    {
        var response = await AdminClient().GetAsync("/api/admin/pricing-parameters");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("key");
        body.Should().Contain("value");
    }

    [Fact]
    public async Task GetAll_WithoutAuth_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/admin/pricing-parameters");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAll_WithCustomerToken_Returns403()
    {
        var response = await CustomerClient().GetAsync("/api/admin/pricing-parameters");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── PUT /api/admin/pricing-parameters/{id} ─────────────────────────────

    [Fact]
    public async Task Update_ExistingParameter_Returns200WithUpdatedValue()
    {
        // Get existing pricing parameters
        var allResp = await AdminClient().GetAsync("/api/admin/pricing-parameters");
        var allBody = await allResp.Content.ReadAsStringAsync();
        var allParams = JsonSerializer.Deserialize<JsonElement[]>(allBody, _json)!;
        if (allParams.Length == 0) return;

        var firstId = allParams[0].GetProperty("id").GetInt32();
        var originalValue = allParams[0].GetProperty("value").GetDecimal();

        var response = await AdminClient().PutAsJsonAsync(
            $"/api/admin/pricing-parameters/{firstId}",
            new { value = originalValue + 1 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(body, _json);
        doc.GetProperty("value").GetDecimal().Should().Be(originalValue + 1);

        // Restore original value
        await AdminClient().PutAsJsonAsync(
            $"/api/admin/pricing-parameters/{firstId}",
            new { value = originalValue });
    }

    [Fact]
    public async Task Update_NonExistentParameter_Returns404()
    {
        var response = await AdminClient().PutAsJsonAsync(
            "/api/admin/pricing-parameters/99999",
            new { value = 100m });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
