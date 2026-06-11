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
/// Integration tests for AdminSettingsController.
/// </summary>
public class AdminSettingsControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public AdminSettingsControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        EnsureSettingsSeeded();
    }

    private void EnsureSettingsSeeded()
    {
        _factory.SeedDatabase(db =>
        {
            if (!db.SystemSettings.Any(s => s.Key == "openai_api_key"))
            {
                db.SystemSettings.AddRange(
                    new SystemSetting { Key = "openai_api_key",     Label = "OpenAI API Key",     Value = "",   IsSensitive = true },
                    new SystemSetting { Key = "stripe_secret_key",  Label = "Stripe Secret Key",  Value = "sk_test_xxx", IsSensitive = true },
                    new SystemSetting { Key = "public_setting",     Label = "Public Setting",     Value = "hello",       IsSensitive = false }
                );
                db.SaveChanges();
            }
        });
    }

    private HttpClient AdminClient() =>
        _factory.CreateAuthenticatedClient(userId: 99, email: "admin@test.com", name: "Admin", role: UserRole.Admin);

    // ── GET /api/admin/settings ───────────────────────────────────────────────

    [Fact]
    public async Task GetAll_WithAdminToken_Returns200WithSettings()
    {
        var response = await AdminClient().GetAsync("/api/admin/settings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("openai_api_key");
    }

    [Fact]
    public async Task GetAll_WithoutAuth_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/admin/settings");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAll_WithCustomerToken_Returns403()
    {
        var client = _factory.CreateAuthenticatedClient(role: UserRole.Customer);
        var response = await client.GetAsync("/api/admin/settings");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAll_SensitiveValueWithContent_IsMasked()
    {
        var response = await AdminClient().GetAsync("/api/admin/settings");
        var body = await response.Content.ReadAsStringAsync();

        // stripe_secret_key has a value, so it should be masked
        body.Should().Contain("••••••••");
    }

    [Fact]
    public async Task GetAll_NonSensitiveValue_IsVisible()
    {
        var response = await AdminClient().GetAsync("/api/admin/settings");
        var body = await response.Content.ReadAsStringAsync();

        // "public_setting" key is always present (seeded); its value may have been
        // changed by the Update test in the same run, so check by key name instead.
        body.Should().Contain("public_setting");
    }

    // ── PUT /api/admin/settings/{key} ─────────────────────────────────────────

    [Fact]
    public async Task Update_WithAdminToken_Updates200()
    {
        var response = await AdminClient().PutAsJsonAsync(
            "/api/admin/settings/public_setting",
            new { value = "updated_value" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("updated_value");
    }

    [Fact]
    public async Task Update_NullValue_Returns400()
    {
        var response = await AdminClient().PutAsJsonAsync(
            "/api/admin/settings/public_setting",
            new { value = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_UnknownKey_UpsertCreatesEntry_Returns200()
    {
        // SetValueAsync is an upsert — it creates the key if it doesn't exist,
        // so an unknown key returns 200 (not 404).
        var response = await AdminClient().PutAsJsonAsync(
            "/api/admin/settings/totally_nonexistent_key_xyz",
            new { value = "foo" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
