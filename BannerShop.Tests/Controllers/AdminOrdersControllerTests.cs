using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests for admin order management HTTP endpoints.
/// Covers GET detail, PUT status, PUT production stage, POST shipping, and filtered/searched list.
/// </summary>
public class AdminOrdersControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public AdminOrdersControllerTests(TestWebApplicationFactory factory)
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

    // ── GET /api/admin/orders/{id} ───────────────────────────────────────────

    [Fact]
    public async Task AdminGetOrder_KnownOrder_Returns200WithOrderDetail()
    {
        var customerClient = RegisterAndGetAuthenticatedClient();
        var orderId = await CreateDraftAndGetId(customerClient);
        var adminClient = _factory.CreateAuthenticatedClient(role: UserRole.Admin);

        var response = await adminClient.GetAsync($"/api/admin/orders/{orderId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.ReadJsonAsync<JsonElement>();
        doc.GetProperty("id").GetInt32().Should().Be(orderId);
        doc.GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);
        doc.GetProperty("status").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AdminGetOrder_UnknownOrder_Returns404()
    {
        var adminClient = _factory.CreateAuthenticatedClient(role: UserRole.Admin);

        var response = await adminClient.GetAsync("/api/admin/orders/99999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── PUT /api/admin/orders/{id}/status ────────────────────────────────────

    [Fact]
    public async Task AdminUpdateStatus_KnownOrder_Returns200WithUpdatedStatus()
    {
        var customerClient = RegisterAndGetAuthenticatedClient();
        var orderId = await CreateDraftAndGetId(customerClient);
        var adminClient = _factory.CreateAuthenticatedClient(role: UserRole.Admin);

        var response = await adminClient.PutAsJsonAsync(
            $"/api/admin/orders/{orderId}/status",
            new { status = "Paid" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.ReadJsonAsync<JsonElement>();
        doc.GetProperty("status").GetString().Should().Be("Paid");
    }

    [Fact]
    public async Task AdminUpdateStatus_UnknownOrder_Returns404()
    {
        var adminClient = _factory.CreateAuthenticatedClient(role: UserRole.Admin);

        var response = await adminClient.PutAsJsonAsync(
            "/api/admin/orders/99999998/status",
            new { status = "Paid" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── PUT /api/admin/orders/{id}/items/{itemId}/production ─────────────────

    [Fact]
    public async Task AdminUpdateProduction_KnownOrderItem_Returns200WithProductionHistory()
    {
        var customerClient = RegisterAndGetAuthenticatedClient();
        var (orderId, itemId) = await CreateDraftAndGetIds(customerClient);
        var adminClient = _factory.CreateAuthenticatedClient(role: UserRole.Admin);

        var response = await adminClient.PutAsJsonAsync(
            $"/api/admin/orders/{orderId}/items/{itemId}/production",
            new { stage = "Printing", notes = "Started on press 1" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.ReadJsonAsync<JsonElement>();
        var item = doc.GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("id").GetInt32() == itemId);
        item.GetProperty("currentProductionStage").GetString().Should().Be("Printing");
    }

    [Fact]
    public async Task AdminUpdateProduction_UnknownItem_Returns404()
    {
        var customerClient = RegisterAndGetAuthenticatedClient();
        var orderId = await CreateDraftAndGetId(customerClient);
        var adminClient = _factory.CreateAuthenticatedClient(role: UserRole.Admin);

        var response = await adminClient.PutAsJsonAsync(
            $"/api/admin/orders/{orderId}/items/99999997/production",
            new { stage = "Printing" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── POST /api/admin/orders/{id}/shipping ─────────────────────────────────

    [Fact]
    public async Task AdminSetShipping_KnownOrder_Returns200AndSetsStatusToShipped()
    {
        var customerClient = RegisterAndGetAuthenticatedClient();
        var orderId = await CreateDraftAndGetId(customerClient);
        var adminClient = _factory.CreateAuthenticatedClient(role: UserRole.Admin);

        // Promote through the required states so the order is shippable
        await adminClient.PutAsJsonAsync($"/api/admin/orders/{orderId}/status", new { status = "Paid" });
        await adminClient.PutAsJsonAsync($"/api/admin/orders/{orderId}/status", new { status = "InProduction" });
        await adminClient.PutAsJsonAsync($"/api/admin/orders/{orderId}/status", new { status = "ReadyToShip" });

        var response = await adminClient.PostAsJsonAsync(
            $"/api/admin/orders/{orderId}/shipping",
            new
            {
                carrier = "Bring",
                trackingNumber = "TEST12345678",
                trackingUrl = "https://tracking.bring.com/tracking/TEST12345678"
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.ReadJsonAsync<JsonElement>();
        doc.GetProperty("status").GetString().Should().Be("Shipped");
        doc.GetProperty("shipmentTracking").GetProperty("carrier").GetString().Should().Be("Bring");
        doc.GetProperty("shipmentTracking").GetProperty("trackingNumber").GetString()
            .Should().Be("TEST12345678");
    }

    [Fact]
    public async Task AdminSetShipping_OrderInNonShippableState_Returns422()
    {
        var customerClient = RegisterAndGetAuthenticatedClient();
        var orderId = await CreateDraftAndGetId(customerClient);
        var adminClient = _factory.CreateAuthenticatedClient(role: UserRole.Admin);
        // Order is in PendingPayment state — not shippable

        var response = await adminClient.PostAsJsonAsync(
            $"/api/admin/orders/{orderId}/shipping",
            new { carrier = "Bring", trackingNumber = "INVALID001" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AdminUpdateStatus_InvalidTransition_Returns422()
    {
        var customerClient = RegisterAndGetAuthenticatedClient();
        var orderId = await CreateDraftAndGetId(customerClient);
        var adminClient = _factory.CreateAuthenticatedClient(role: UserRole.Admin);
        // PendingPayment → Delivered skips many intermediate states

        var response = await adminClient.PutAsJsonAsync(
            $"/api/admin/orders/{orderId}/status",
            new { status = "Delivered" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AdminSetShipping_UnknownOrder_Returns404()
    {
        var adminClient = _factory.CreateAuthenticatedClient(role: UserRole.Admin);

        var response = await adminClient.PostAsJsonAsync(
            "/api/admin/orders/99999996/shipping",
            new { carrier = "Bring", trackingNumber = "NOTFOUND001" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── GET /api/admin/orders?status=Paid ────────────────────────────────────

    [Fact]
    public async Task AdminListOrders_FilterByStatus_OnlyReturnsOrdersWithThatStatus()
    {
        // Create an order, promote it to Paid
        var customerClient = RegisterAndGetAuthenticatedClient();
        var orderId = await CreateDraftAndGetId(customerClient);
        var adminClient = _factory.CreateAuthenticatedClient(role: UserRole.Admin);
        await adminClient.PutAsJsonAsync(
            $"/api/admin/orders/{orderId}/status",
            new { status = "Paid" });

        var response = await adminClient.GetAsync("/api/admin/orders?status=Paid&pageSize=200");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.ReadJsonAsync<JsonElement>();
        var items = doc.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0);
        // Every returned item should have Paid status
        foreach (var item in items.EnumerateArray())
            item.GetProperty("status").GetString().Should().Be("Paid");
    }

    // ── GET /api/admin/orders?search=... ─────────────────────────────────────

    [Fact]
    public async Task AdminListOrders_SearchByOrderId_ReturnsMatchingOrder()
    {
        // Create an order and search for it by its numeric ID.
        // Searching by a numeric string takes the o.Id == maybeId path first,
        // so if the target order is found by ID the query succeeds without
        // needing to resolve User navigation properties on every row.
        var customerClient = RegisterAndGetAuthenticatedClient();
        var orderId = await CreateDraftAndGetId(customerClient);
        var adminClient = _factory.CreateAuthenticatedClient(role: UserRole.Admin);

        var response = await adminClient.GetAsync($"/api/admin/orders?search={orderId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.ReadJsonAsync<JsonElement>();
        doc.GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);
        var found = doc.GetProperty("items").EnumerateArray()
            .Any(i => i.GetProperty("id").GetInt32() == orderId);
        found.Should().BeTrue($"order {orderId} should appear in search results for query '{orderId}'");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Registers a fresh customer user and returns an authenticated HttpClient.</summary>
    private HttpClient RegisterAndGetAuthenticatedClient(out string email)
    {
        email = $"adminorders_{Guid.NewGuid():N}@test.com";
        var localEmail = email;
        var client = _factory.CreateClient();

        var task = client.PostAsJsonAsync("/api/auth/register",
            new { email = localEmail, password = "Pass123!", name = "Test Customer" });
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

    private static object BuildDraftRequest(int bannerSizeId = 1) => new
    {
        deliveryType = "Standard",
        shippingAddress = new { line1 = "Admin Test St 1", postalCode = "0001", city = "Oslo", country = "NO" },
        items = new[] { new { bannerSizeId, quantity = 1 } }
    };

    /// <summary>Creates a draft order and returns its orderId.</summary>
    private static async Task<int> CreateDraftAndGetId(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/api/orders/draft", BuildDraftRequest());
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(body, _json);
        return doc.GetProperty("orderId").GetInt32();
    }

    /// <summary>Creates a draft order and returns both (orderId, first itemId).</summary>
    private async Task<(int orderId, int itemId)> CreateDraftAndGetIds(HttpClient client)
    {
        var orderId = await CreateDraftAndGetId(client);

        // Fetch the order detail via admin to get the item ID
        var adminClient = _factory.CreateAuthenticatedClient(role: UserRole.Admin);
        var detailResp = await adminClient.GetAsync($"/api/admin/orders/{orderId}");
        detailResp.EnsureSuccessStatusCode();
        var detailBody = await detailResp.Content.ReadAsStringAsync();
        var detail = JsonSerializer.Deserialize<JsonElement>(detailBody, _json);
        var itemId = detail.GetProperty("items")[0].GetProperty("id").GetInt32();
        return (orderId, itemId);
    }
}
