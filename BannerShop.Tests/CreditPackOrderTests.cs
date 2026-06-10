using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BannerShop.Api.Services.Orders;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// BANNERSH-139: end-to-end tests covering credit-pack purchases tracked as Orders.
///
/// Verifies that:
///  1. POST /api/ai-credits/packs/buy creates an Order row with OrderType=CreditPack.
///  2. The admin orders list HIDES credit-pack orders by default.
///  3. Passing <c>includeCreditPacks=true</c> reveals them.
///  4. Filtering by <c>orderType=CreditPack</c> also reveals them (no flag needed).
///  5. The Stripe webhook flips the Order to Paid AND grants credits (via the
///     credit-service path already covered by <c>WebhookCreditPackTests</c>).
/// </summary>
public class CreditPackOrderTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public CreditPackOrderTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SeedDatabase(db =>
        {
            if (!db.PricingParameters.Any())
                DbHelper.SeedPricingParameters(db);
        });
    }

    // ── 1. Buy creates an Order row with OrderType=CreditPack ────────────────

    [Fact]
    public async Task BuyCreditPack_CreatesOrderRow_WithCreditPackType()
    {
        var client = RegisterAndGetAuthenticatedClient(out var userId);

        var resp = await client.PostAsJsonAsync("/api/ai-credits/packs/buy", new { pack = "small" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(body, _json);
        var orderId = doc.GetProperty("orderId").GetInt32();
        orderId.Should().BeGreaterThan(0, "the buy endpoint must return the synthetic Order id (BANNERSH-139)");

        // Verify it landed in the DB with the right shape
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BannerShop.Infrastructure.Data.BannerShopDbContext>();
        var order = db.Orders.Single(o => o.Id == orderId);
        order.OrderType.Should().Be(OrderType.CreditPack);
        order.UserId.Should().Be(userId);
        order.TotalNok.Should().Be(29m, "small pack costs 29 kr");
        order.StripePaymentIntentId.Should().NotBeNullOrEmpty(
            "the synthetic order must be linked to the PI for webhook resolution");

        // Synthetic line item should describe the pack
        var items = db.OrderItems.Where(i => i.OrderId == orderId).ToList();
        items.Should().HaveCount(1);
        items[0].UnitPriceNok.Should().Be(29m);
        items[0].Notes.Should().Contain("5 credits");
    }

    [Fact]
    public async Task BuyCreditPack_LargeTier_CreatesOrderWithLargePrice()
    {
        var client = RegisterAndGetAuthenticatedClient(out _);

        var resp = await client.PostAsJsonAsync("/api/ai-credits/packs/buy", new { pack = "large" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(body, _json);
        var orderId = doc.GetProperty("orderId").GetInt32();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BannerShop.Infrastructure.Data.BannerShopDbContext>();
        var order = db.Orders.Single(o => o.Id == orderId);
        order.OrderType.Should().Be(OrderType.CreditPack);
        order.TotalNok.Should().Be(95m, "large pack costs 95 kr");

        var item = db.OrderItems.Single(i => i.OrderId == orderId);
        item.Notes.Should().Contain("20 credits");
    }

    // ── 2. Admin list hides credit-pack orders by default ────────────────────

    [Fact]
    public async Task AdminListOrders_DefaultHidesCreditPackOrders()
    {
        var client = RegisterAndGetAuthenticatedClient(out _);
        // Buy a small pack → creates a CreditPack order
        var buyResp = await client.PostAsJsonAsync("/api/ai-credits/packs/buy", new { pack = "small" });
        buyResp.EnsureSuccessStatusCode();
        var buyDoc = JsonSerializer.Deserialize<JsonElement>(
            await buyResp.Content.ReadAsStringAsync(), _json);
        var creditPackOrderId = buyDoc.GetProperty("orderId").GetInt32();

        var admin = _factory.CreateAuthenticatedClient(role: UserRole.Admin);
        var listResp = await admin.GetAsync("/api/admin/orders?pageSize=500");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = JsonSerializer.Deserialize<JsonElement>(
            await listResp.Content.ReadAsStringAsync(), _json);
        var ids = doc.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetInt32()).ToList();
        ids.Should().NotContain(creditPackOrderId,
            "credit-pack orders must be hidden from the default admin list (BANNERSH-139)");
    }

    // ── 3. Opt-in flag reveals credit packs ──────────────────────────────────

    [Fact]
    public async Task AdminListOrders_WithIncludeCreditPacksTrue_ReturnsCreditPackOrders()
    {
        var client = RegisterAndGetAuthenticatedClient(out _);
        var buyResp = await client.PostAsJsonAsync("/api/ai-credits/packs/buy", new { pack = "small" });
        buyResp.EnsureSuccessStatusCode();
        var buyDoc = JsonSerializer.Deserialize<JsonElement>(
            await buyResp.Content.ReadAsStringAsync(), _json);
        var creditPackOrderId = buyDoc.GetProperty("orderId").GetInt32();

        var admin = _factory.CreateAuthenticatedClient(role: UserRole.Admin);
        var listResp = await admin.GetAsync(
            "/api/admin/orders?pageSize=500&includeCreditPacks=true");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = JsonSerializer.Deserialize<JsonElement>(
            await listResp.Content.ReadAsStringAsync(), _json);
        var ids = doc.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetInt32()).ToList();
        ids.Should().Contain(creditPackOrderId,
            "includeCreditPacks=true must surface credit-pack orders (BANNERSH-139)");
    }

    // ── 4. Explicit orderType=CreditPack filter reveals them ─────────────────

    [Fact]
    public async Task AdminListOrders_FilterByCreditPackType_ReturnsOnlyCreditPackOrders()
    {
        var client = RegisterAndGetAuthenticatedClient(out _);
        var buyResp = await client.PostAsJsonAsync("/api/ai-credits/packs/buy", new { pack = "small" });
        buyResp.EnsureSuccessStatusCode();
        var buyDoc = JsonSerializer.Deserialize<JsonElement>(
            await buyResp.Content.ReadAsStringAsync(), _json);
        var creditPackOrderId = buyDoc.GetProperty("orderId").GetInt32();

        var admin = _factory.CreateAuthenticatedClient(role: UserRole.Admin);
        var listResp = await admin.GetAsync(
            "/api/admin/orders?orderType=CreditPack&pageSize=500");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = JsonSerializer.Deserialize<JsonElement>(
            await listResp.Content.ReadAsStringAsync(), _json);
        var items = doc.GetProperty("items").EnumerateArray().ToList();
        items.Should().NotBeEmpty();
        // Every returned row must be a CreditPack and our order must be present
        foreach (var item in items)
            item.GetProperty("orderType").GetString().Should().Be("CreditPack");
        items.Any(i => i.GetProperty("id").GetInt32() == creditPackOrderId)
            .Should().BeTrue("the just-created credit-pack order must appear");
    }

    // ── 5. ListAllAsync service-level filter behaviour ───────────────────────

    [Fact]
    public async Task OrderService_ListAllAsync_DefaultExcludesCreditPackOrders()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BannerShop.Infrastructure.Data.BannerShopDbContext>();
        var orderService = scope.ServiceProvider.GetRequiredService<IAdminOrderService>();

        // Add a credit-pack order directly
        var user = new BannerShop.Core.Entities.User
        {
            Email = $"directuser_{Guid.NewGuid():N}@test.com",
            Name = "Direct User",
            PasswordHash = "test-hash",
            Role = UserRole.Customer,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var creditPackOrder = new BannerShop.Core.Entities.Order
        {
            UserId       = user.Id,
            OrderType    = OrderType.CreditPack,
            OrderState   = OrderState.Paid,
            Status       = OrderStatus.Paid,
            DeliveryType = DeliveryType.Pickup,
            TotalNok     = 29m,
            CreatedAt    = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow
        };
        db.Orders.Add(creditPackOrder);
        await db.SaveChangesAsync();

        // Default filter — should NOT include credit-pack rows
        var defaultList = await orderService.ListAllAsync(new AdminOrderFilter { PageSize = 500 });
        defaultList.Items.Select(i => i.Id).Should().NotContain(creditPackOrder.Id);

        // Opt-in flag — should include them
        var optedIn = await orderService.ListAllAsync(new AdminOrderFilter
        {
            PageSize = 500,
            IncludeCreditPacks = true
        });
        optedIn.Items.Select(i => i.Id).Should().Contain(creditPackOrder.Id);

        // Explicit type filter — should include them even without the opt-in flag
        var typeFiltered = await orderService.ListAllAsync(new AdminOrderFilter
        {
            PageSize = 500,
            OrderType = OrderType.CreditPack
        });
        typeFiltered.Items.Select(i => i.Id).Should().Contain(creditPackOrder.Id);
        // Every row should be a credit pack
        foreach (var item in typeFiltered.Items)
            item.OrderType.Should().Be("CreditPack");
    }

    // ── 6. MarkPaidAsync via webhook flips order to Paid ─────────────────────

    [Fact]
    public async Task MarkPaidAsync_OnCreditPackOrder_FlipsToPaidWithoutProductionRows()
    {
        var client = RegisterAndGetAuthenticatedClient(out _);
        var buyResp = await client.PostAsJsonAsync("/api/ai-credits/packs/buy", new { pack = "small" });
        buyResp.EnsureSuccessStatusCode();
        var buyDoc = JsonSerializer.Deserialize<JsonElement>(
            await buyResp.Content.ReadAsStringAsync(), _json);
        var orderId = buyDoc.GetProperty("orderId").GetInt32();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BannerShop.Infrastructure.Data.BannerShopDbContext>();
        var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();

        // Sanity: pre-state
        var orderBefore = db.Orders.Single(o => o.Id == orderId);
        orderBefore.Status.Should().BeOneOf(OrderStatus.Draft, OrderStatus.PendingPayment);

        // Simulate the webhook calling MarkPaidAsync
        var pi = orderBefore.StripePaymentIntentId!;
        await orderService.MarkPaidAsync(pi, orderId, CancellationToken.None);

        // Re-read from a fresh context to bypass tracking
        using var verifyScope = _factory.Services.CreateScope();
        var freshDb = verifyScope.ServiceProvider.GetRequiredService<BannerShop.Infrastructure.Data.BannerShopDbContext>();
        var orderAfter = freshDb.Orders.Single(o => o.Id == orderId);
        orderAfter.Status.Should().Be(OrderStatus.Paid);
        orderAfter.OrderState.Should().Be(OrderState.Paid);

        // No ProductionStatus rows should have been seeded for credit-pack orders
        var prodRows = freshDb.ProductionStatuses
            .Where(p => freshDb.OrderItems.Any(i => i.Id == p.OrderItemId && i.OrderId == orderId))
            .ToList();
        prodRows.Should().BeEmpty(
            "credit-pack orders never enter production, so MarkPaidAsync must not seed Queued rows");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private HttpClient RegisterAndGetAuthenticatedClient(out int userId)
    {
        var email = $"creditpack_{Guid.NewGuid():N}@test.com";
        var client = _factory.CreateClient();

        var regResp = client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Pass123!", name = "Credit Pack User" }).GetAwaiter().GetResult();
        regResp.EnsureSuccessStatusCode();
        var body = regResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var json = JsonSerializer.Deserialize<JsonElement>(body, _json);
        var token = json.GetProperty("accessToken").GetString();
        userId = json.GetProperty("user").GetProperty("id").GetInt32();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
