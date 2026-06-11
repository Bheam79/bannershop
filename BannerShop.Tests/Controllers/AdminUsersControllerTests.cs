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
/// Integration tests for AdminUsersController.
/// </summary>
public class AdminUsersControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public AdminUsersControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        EnsureUsersSeeded();
    }

    private void EnsureUsersSeeded()
    {
        _factory.SeedDatabase(db =>
        {
            if (!db.Users.Any(u => u.Email == "alice@test.com"))
            {
                db.Users.AddRange(
                    new User
                    {
                        Id = 500, Email = "alice@test.com", Name = "Alice", PasswordHash = "x",
                        Role = UserRole.Customer, AiCreditsRemaining = 3, HasUsedFreeAiGeneration = false,
                        CreatedAt = DateTime.UtcNow
                    },
                    new User
                    {
                        Id = 501, Email = "bob@test.com", Name = "Bob Smith", PasswordHash = "x",
                        Role = UserRole.Customer, Phone = "12345678", AiCreditsRemaining = 0,
                        HasUsedFreeAiGeneration = true, CreatedAt = DateTime.UtcNow
                    }
                );
                db.SaveChanges();
            }
        });
    }

    private HttpClient AdminClient() =>
        _factory.CreateAuthenticatedClient(userId: 99, email: "admin@test.com", name: "Admin", role: UserRole.Admin);

    // ── GET /api/admin/users ──────────────────────────────────────────────────

    [Fact]
    public async Task List_WithAdminToken_Returns200WithPaginatedUsers()
    {
        var response = await AdminClient().GetAsync("/api/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("items");
        body.Should().Contain("totalCount");
    }

    [Fact]
    public async Task List_WithoutAuth_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_WithCustomerToken_Returns403()
    {
        var client = _factory.CreateAuthenticatedClient(role: UserRole.Customer);
        var response = await client.GetAsync("/api/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_WithSearchByEmail_FiltersResults()
    {
        var response = await AdminClient().GetAsync("/api/admin/users?search=alice%40test.com");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("alice@test.com");
    }

    [Fact]
    public async Task List_WithSearchByName_FiltersResults()
    {
        var response = await AdminClient().GetAsync("/api/admin/users?search=Bob");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Bob");
    }

    [Fact]
    public async Task List_WithSearchById_FiltersResults()
    {
        var response = await AdminClient().GetAsync("/api/admin/users?search=500");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("alice");
    }

    [Fact]
    public async Task List_WithPagination_RespectsPageSize()
    {
        var response = await AdminClient().GetAsync("/api/admin/users?page=1&pageSize=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(body, _json);
        doc.GetProperty("items").GetArrayLength().Should().BeLessThanOrEqualTo(1);
    }

    // ── GET /api/admin/users/{id} ─────────────────────────────────────────────

    [Fact]
    public async Task Get_ExistingUser_Returns200WithDetail()
    {
        var response = await AdminClient().GetAsync("/api/admin/users/500");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("alice@test.com");
        body.Should().Contain("recentCreditTransactions");
    }

    [Fact]
    public async Task Get_NonExistentUser_Returns404()
    {
        var response = await AdminClient().GetAsync("/api/admin/users/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST /api/admin/users/{id}/grant-credits ──────────────────────────────

    [Fact]
    public async Task GrantCredits_ValidRequest_Returns200WithUpdatedDetail()
    {
        var response = await AdminClient().PostAsJsonAsync(
            "/api/admin/users/500/grant-credits",
            new { amount = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("alice@test.com");
    }

    [Fact]
    public async Task GrantCredits_NonExistentUser_Returns404()
    {
        var response = await AdminClient().PostAsJsonAsync(
            "/api/admin/users/99999/grant-credits",
            new { amount = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
