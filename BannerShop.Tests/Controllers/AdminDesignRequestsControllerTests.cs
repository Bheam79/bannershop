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
/// Integration tests for AdminDesignRequestsController.
/// </summary>
public class AdminDesignRequestsControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public AdminDesignRequestsControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        EnsureSeed();
    }

    private void EnsureSeed()
    {
        _factory.SeedDatabase(db =>
        {
            if (!db.BannerTemplates.Any(t => t.Id == 200))
            {
                db.BannerTemplates.Add(new BannerTemplate
                {
                    Id = 200, Category = BannerTemplateCategory.Birthday,
                    NameNb = "Bursdag", NameEn = "Birthday", SortOrder = 10
                });
                db.Users.Add(new User
                {
                    Id = 600, Email = "customer600@test.com", Name = "Test Customer 600",
                    PasswordHash = "x", Role = UserRole.Customer, CreatedAt = DateTime.UtcNow
                });
                db.DesignRequests.AddRange(
                    new DesignRequest
                    {
                        Id = 1001, UserId = 600, BannerTemplateId = 200,
                        Mode = DesignRequestMode.Ai, Language = "nb",
                        PersonName = "Ole", TextContent = "Gratulerer", ThemeDescription = "Tropical",
                        AspectRatio = "16:9", Status = DesignRequestStatus.Pending,
                        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                    },
                    new DesignRequest
                    {
                        Id = 1002, UserId = 600, BannerTemplateId = 200,
                        Mode = DesignRequestMode.Manual, Language = "nb",
                        PersonName = "Kari", TextContent = "Lykke til", ThemeDescription = "Elegant",
                        AspectRatio = "18:9", Status = DesignRequestStatus.AwaitingApproval,
                        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                    }
                );
                db.SaveChanges();
            }
        });
    }

    private HttpClient AdminClient() =>
        _factory.CreateAuthenticatedClient(userId: 99, email: "admin@test.com", name: "Admin", role: UserRole.Admin);

    // ── GET /api/admin/design-requests ────────────────────────────────────────

    [Fact]
    public async Task List_WithAdminToken_Returns200()
    {
        var response = await AdminClient().GetAsync("/api/admin/design-requests");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("items");
    }

    [Fact]
    public async Task List_WithoutAuth_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/admin/design-requests");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_WithCustomerToken_Returns403()
    {
        var client = _factory.CreateAuthenticatedClient(role: UserRole.Customer);
        var response = await client.GetAsync("/api/admin/design-requests");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_FilterByStatus_Returns200()
    {
        var response = await AdminClient().GetAsync("/api/admin/design-requests?status=Pending");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task List_FilterByMode_Returns200()
    {
        var response = await AdminClient().GetAsync("/api/admin/design-requests?mode=Ai");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GET /api/admin/design-requests/{id} ──────────────────────────────────

    [Fact]
    public async Task Get_ExistingRequest_Returns200()
    {
        var response = await AdminClient().GetAsync("/api/admin/design-requests/1001");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Ole");
    }

    [Fact]
    public async Task Get_NonExistentRequest_Returns404()
    {
        var response = await AdminClient().GetAsync("/api/admin/design-requests/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── PUT /api/admin/design-requests/{id}/status ────────────────────────────

    [Fact]
    public async Task UpdateStatus_ValidTransition_Returns200()
    {
        // Admin-settable statuses: InProgress, AwaitingApproval, Revised, Final, Cancelled
        // "Approved" is a customer-set status and is rejected by the service.
        var response = await AdminClient().PutAsJsonAsync(
            "/api/admin/design-requests/1002/status",
            new { status = "InProgress", notes = "Working on it" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateStatus_NonExistentRequest_Returns400()
    {
        var response = await AdminClient().PutAsJsonAsync(
            "/api/admin/design-requests/99999/status",
            new { status = "Approved", notes = "" });

        // Service returns error → BadRequest
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── POST /api/admin/design-requests/{id}/upscale ──────────────────────────

    [Fact]
    public async Task Upscale_WithoutAuth_Returns401()
    {
        var response = await _factory.CreateClient().PostAsync("/api/admin/design-requests/1/upscale", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Upscale_NonExistentRequest_Returns400OrUnavailable()
    {
        // Upscaler is not configured in tests → 503; if no such DR → 400
        var response = await AdminClient().PostAsync("/api/admin/design-requests/99999/upscale?scale=4", null);

        // 400 = not found error, 503 = upscaler not configured
        ((int)response.StatusCode).Should().BeOneOf(400, 503);
    }
}
