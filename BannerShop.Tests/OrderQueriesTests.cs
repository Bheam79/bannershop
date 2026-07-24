using BannerShop.Api.Services.Orders;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Unit tests for <see cref="OrderQueries"/> — the shared EF Core eager-loading
/// helpers (BANNERSH-199) used by both <see cref="BannerShop.Api.Services.Orders.OrderService"/>
/// and <see cref="BannerShop.Api.Services.Orders.AdminOrderService"/>.
/// </summary>
public class OrderQueriesTests
{
    [Fact]
    public async Task LoadFullOrderAsync_Returns_Null_When_Order_Not_Found()
    {
        var db = DbHelper.CreateInMemory();

        var result = await OrderQueries.LoadFullOrderAsync(db, 999, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadFullOrderAsync_Eager_Loads_Full_Navigation_Graph()
    {
        var db = DbHelper.CreateInMemory();
        var user = DbHelper.MakeUser(1);
        var material = DbHelper.MakeMaterial(1);
        var size = DbHelper.MakeSizeRule(1, material, 1, 700, 1, 154, 154);
        var address = new Address
        {
            Id = 1,
            UserId = 1,
            Line1 = "Test Street 1",
            PostalCode = "0001",
            City = "Oslo",
            Country = "Norge"
        };
        db.Users.Add(user);
        db.Materials.Add(material);
        db.BannerSizes.Add(size);
        db.Addresses.Add(address);

        var order = new Order { Id = 1, UserId = 1, ShippingAddressId = 1 };
        db.Orders.Add(order);

        var design = new BannerDesign { Id = 1, UserId = 1, StoragePath = "designs/1.png" };
        db.BannerDesigns.Add(design);

        var designRequest = new DesignRequest { Id = 1, UserId = 1, OrderId = 1, Mode = DesignRequestMode.Ai };
        db.DesignRequests.Add(designRequest);

        var item = new OrderItem
        {
            Id = 1,
            OrderId = 1,
            BannerSizeId = 1,
            HeightCm = 150,
            Quantity = 1,
            BannerDesignId = 1,
            DesignRequestId = 1
        };
        db.OrderItems.Add(item);

        db.ProductionStatuses.Add(new ProductionStatus { Id = 1, OrderItemId = 1, Stage = ProductionStage.Printing });

        var tracking = new ShipmentTracking { Id = 1, OrderId = 1, TrackingNumber = "TR-1" };
        db.ShipmentTrackings.Add(tracking);

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await OrderQueries.LoadFullOrderAsync(db, 1, CancellationToken.None);

        result.Should().NotBeNull();
        result!.User.Should().NotBeNull();
        result.User.Id.Should().Be(1);
        result.ShippingAddress.Should().NotBeNull();
        result.ShipmentTracking.Should().NotBeNull();
        result.Items.Should().HaveCount(1);

        var loadedItem = result.Items.Single();
        loadedItem.BannerSize.Should().NotBeNull();
        loadedItem.BannerSize!.Material.Should().NotBeNull();
        loadedItem.BannerDesign.Should().NotBeNull();
        loadedItem.DesignRequest.Should().NotBeNull();
        loadedItem.ProductionStatuses.Should().HaveCount(1);
    }

    [Fact]
    public async Task LoadDesignRequestForOrderAsync_Returns_Null_When_No_Linked_Request()
    {
        var db = DbHelper.CreateInMemory();
        db.Orders.Add(new Order { Id = 1, UserId = 1 });
        await db.SaveChangesAsync();

        var result = await OrderQueries.LoadDesignRequestForOrderAsync(db, 1, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadDesignRequestForOrderAsync_Returns_Linked_Request()
    {
        var db = DbHelper.CreateInMemory();
        db.DesignRequests.Add(new DesignRequest { Id = 1, OrderId = 42, Mode = DesignRequestMode.Manual });
        db.DesignRequests.Add(new DesignRequest { Id = 2, OrderId = 43, Mode = DesignRequestMode.Ai });
        await db.SaveChangesAsync();

        var result = await OrderQueries.LoadDesignRequestForOrderAsync(db, 42, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
    }

    [Fact]
    public async Task LoadDesignRequestsForOrdersAsync_Returns_Empty_Dictionary_For_Empty_Input()
    {
        var db = DbHelper.CreateInMemory();

        var result = await OrderQueries.LoadDesignRequestsForOrdersAsync(db, Array.Empty<int>(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadDesignRequestsForOrdersAsync_Keys_By_OrderId_And_Skips_Unlinked_Orders()
    {
        var db = DbHelper.CreateInMemory();
        db.DesignRequests.Add(new DesignRequest { Id = 1, OrderId = 10, Mode = DesignRequestMode.Ai });
        db.DesignRequests.Add(new DesignRequest { Id = 2, OrderId = 20, Mode = DesignRequestMode.Manual });
        db.DesignRequests.Add(new DesignRequest { Id = 3, OrderId = null, Mode = DesignRequestMode.Ai });
        await db.SaveChangesAsync();

        var result = await OrderQueries.LoadDesignRequestsForOrdersAsync(db, new[] { 10, 20, 30 }, CancellationToken.None);

        result.Should().HaveCount(2);
        result[10].Id.Should().Be(1);
        result[20].Id.Should().Be(2);
        result.Should().NotContainKey(30);
    }

    [Fact]
    public async Task LoadDesignRequestsForOrdersAsync_Keeps_First_When_Multiple_Requests_Share_An_Order()
    {
        var db = DbHelper.CreateInMemory();
        db.DesignRequests.Add(new DesignRequest { Id = 1, OrderId = 10, Mode = DesignRequestMode.Ai });
        db.DesignRequests.Add(new DesignRequest { Id = 2, OrderId = 10, Mode = DesignRequestMode.Manual });
        await db.SaveChangesAsync();

        var result = await OrderQueries.LoadDesignRequestsForOrdersAsync(db, new[] { 10 }, CancellationToken.None);

        result.Should().HaveCount(1);
        result[10].Id.Should().Be(1);
    }
}
