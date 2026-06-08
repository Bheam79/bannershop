using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests for OrdersController.
/// Covers auth guards, basic happy paths and validation errors.
/// </summary>
public class OrdersControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public OrdersControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        EnsureCatalogAndParamsSeeded();
    }

    private void EnsureCatalogAndParamsSeeded()
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

    // ── Auth guard ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDraft_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            deliveryType = "Standard",
            shippingAddress = new { line1 = "Test St", postalCode = "0001", city = "Oslo" },
            items = new[] { new { bannerSizeId = 1, quantity = 1 } }
        };

        var response = await client.PostAsJsonAsync("/api/orders/draft", req);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListOrders_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/orders");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOrder_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/orders/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Happy paths (authenticated) ──────────────────────────────────────────

    [Fact]
    public async Task ListOrders_WithAuth_Returns200()
    {
        var client = RegisterAndGetAuthenticatedClient();

        var response = await client.GetAsync("/api/orders");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateDraft_ValidRequest_Returns200WithOrderId()
    {
        var client = RegisterAndGetAuthenticatedClient();
        var req = BuildDraftRequest(bannerSizeId: 1);

        var response = await client.PostAsJsonAsync("/api/orders/draft", req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("orderId");
        body.Should().Contain("totalNok");
    }

    [Fact]
    public async Task CreateDraft_Express_IncludesExpressFeeInBreakdown()
    {
        var client = RegisterAndGetAuthenticatedClient();
        var req = BuildDraftRequest(bannerSizeId: 1, deliveryType: "Express");

        var response = await client.PostAsJsonAsync("/api/orders/draft", req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(body, _json);
        doc.GetProperty("breakdown").GetProperty("expressFeeNok").GetDecimal()
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetOrder_OwnOrder_Returns200()
    {
        var client = RegisterAndGetAuthenticatedClient(out var email);
        var draft = await CreateDraftAndGetId(client, bannerSizeId: 1);

        var response = await client.GetAsync($"/api/orders/{draft}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetOrder_OtherUsersOrder_Returns404()
    {
        // User A creates an order
        var clientA = RegisterAndGetAuthenticatedClient();
        var orderId = await CreateDraftAndGetId(clientA, bannerSizeId: 1);

        // User B tries to read it
        var clientB = RegisterAndGetAuthenticatedClient();

        var response = await clientB.GetAsync($"/api/orders/{orderId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CancelOrder_OwnOrder_Returns200WithCancelledStatus()
    {
        var client = RegisterAndGetAuthenticatedClient();
        var orderId = await CreateDraftAndGetId(client, bannerSizeId: 1);

        var response = await client.PostAsync($"/api/orders/{orderId}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Cancelled");
    }

    // ── Validation errors ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDraft_EmptyItems_Returns400()
    {
        var client = RegisterAndGetAuthenticatedClient();
        var req = new
        {
            deliveryType = "Standard",
            shippingAddress = new { line1 = "St", postalCode = "0001", city = "Oslo" },
            items = Array.Empty<object>()
        };

        var response = await client.PostAsJsonAsync("/api/orders/draft", req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateDraft_InvalidBannerSize_Returns400()
    {
        var client = RegisterAndGetAuthenticatedClient();
        var req = BuildDraftRequest(bannerSizeId: 99999);

        var response = await client.PostAsJsonAsync("/api/orders/draft", req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateDraft_CustomWidthSizeWithoutWidth_Returns400()
    {
        // Size 6 requires customWidthCm
        var client = RegisterAndGetAuthenticatedClient();
        var req = BuildDraftRequest(bannerSizeId: 6); // no customWidthCm

        var response = await client.PostAsJsonAsync("/api/orders/draft", req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpClient RegisterAndGetAuthenticatedClient(out string email)
    {
        email = $"orders_{Guid.NewGuid():N}@test.com";
        var localEmail = email;
        var client = _factory.CreateClient();

        // Register synchronously using GetAwaiter so the test body can be sync in spirit
        var task = client.PostAsJsonAsync("/api/auth/register",
            new { email = localEmail, password = "Pass123!", name = "Orders User" });
        var regResp = task.GetAwaiter().GetResult();
        var body = regResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var json = JsonSerializer.Deserialize<JsonElement>(body, _json);
        var accessToken = json.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private HttpClient RegisterAndGetAuthenticatedClient()
        => RegisterAndGetAuthenticatedClient(out _);

    private static async Task<int> CreateDraftAndGetId(HttpClient client, int bannerSizeId)
    {
        var req = BuildDraftRequest(bannerSizeId);
        var resp = await client.PostAsJsonAsync("/api/orders/draft", req);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(body, _json);
        return doc.GetProperty("orderId").GetInt32();
    }

    private static object BuildDraftRequest(int bannerSizeId, string deliveryType = "Standard", int? customWidthCm = null)
    {
        var item = customWidthCm.HasValue
            ? (object)new { bannerSizeId, quantity = 1, customWidthCm }
            : new { bannerSizeId, quantity = 1 };

        return new
        {
            deliveryType,
            shippingAddress = new { line1 = "Test Street 1", postalCode = "0001", city = "Oslo", country = "NO" },
            items = new[] { item }
        };
    }
}
