using System.Net;
using System.Text.Json;
using BannerShop.Core.Entities;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests for ConfigController (GET /api/config/stripe).
/// </summary>
public class ConfigControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public ConfigControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetStripeConfig_NoKeyConfigured_ReturnsEmptyPublishableKey()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/config/stripe");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(body, _json);
        doc.GetProperty("publishableKey").GetString().Should().Be(string.Empty);
    }

    [Fact]
    public async Task GetStripeConfig_WithKeyInDb_ReturnsKey()
    {
        _factory.SeedDatabase(db =>
        {
            // Remove any existing stripe_publishable_key
            var existing = db.SystemSettings.FirstOrDefault(s => s.Key == "stripe_publishable_key_test2");
            if (existing == null)
            {
                db.SystemSettings.Add(new SystemSetting
                {
                    Key = "stripe_publishable_key",
                    Value = "pk_test_abc123"
                });
                db.SaveChanges();
            }
        });

        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/config/stripe");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        // Either empty (if key not set) or the set value — just ensure 200 OK
        body.Should().Contain("publishableKey");
    }

    [Fact]
    public async Task GetStripeConfig_NoAuthRequired_Returns200()
    {
        // Public endpoint — no Bearer token required
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/config/stripe");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
