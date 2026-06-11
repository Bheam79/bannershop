using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.Orders;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Unit tests for the static OrderMapper (ToListItemDto, ToDetailDto).
/// Pure mapping logic — no DB, no HTTP.
/// </summary>
public class OrderMapperTests
{
    private static BannerFileStorage MakeStorage()
    {
        var opts = Options.Create(new FileStorageOptions
        {
            LocalRoot     = "/tmp/test-storage",
            PublicBaseUrl = "https://example.com/uploads",
            MaxUploadBytes = 50L * 1024 * 1024
        });
        return new BannerFileStorage(opts);
    }

    private static Material MakeMaterial() => new()
    {
        Id = 1, Name = "400g Frontlit", WidthCm = 160, MaxBannerWidthCm = 160, WeightGsm = 400, PricePerSqm = 180m
    };

    private static BannerSize MakeSize(Material mat) => new()
    {
        Id = 1, Name = "300 × 150 cm", WidthCm = 300, HeightCm = 150,
        IsActive = true, MaterialId = mat.Id, Material = mat
    };

    private static OrderItem MakeItem(BannerSize size) => new()
    {
        Id = 10, BannerSizeId = size.Id, BannerSize = size,
        HeightCm = 150, Quantity = 1, AreaSqm = 4.5m,
        UnitPriceNok = 540m, EyeletOption = EyeletOption.None,
        EyeletCount = 0, EyeletFeeNok = 0m, LineTotalNok = 540m
    };

    private static Order MakeOrder(OrderType type = OrderType.CustomBanner)
    {
        var user = new User { Id = 1, Name = "Test User", Email = "test@example.com", PasswordHash = "x", Role = UserRole.Customer };
        var mat  = MakeMaterial();
        var size = MakeSize(mat);
        var item = MakeItem(size);

        return new Order
        {
            Id = 100,
            User = user,
            UserId = user.Id,
            Status = OrderStatus.Paid,
            OrderType = type,
            OrderState = OrderState.Paid,
            DeliveryType = DeliveryType.Standard,
            PackingMode = PackingMode.Folded,
            ShippingCostNok = 199m,
            ExpressFeeNok = 0m,
            AiActivationFeeNok = 0m,
            TotalNok = 739m,
            CreatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            EstimatedDelivery = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            Items = new List<OrderItem> { item }
        };
    }

    // ── ToListItemDto ─────────────────────────────────────────────────────────

    [Fact]
    public void ToListItemDto_MapsScalarFields()
    {
        var order = MakeOrder();
        var storage = MakeStorage();

        var dto = OrderMapper.ToListItemDto(order, null, storage);

        dto.Id.Should().Be(100);
        dto.TotalNok.Should().Be(739m);
        dto.ItemCount.Should().Be(1);
        dto.CustomerName.Should().Be("Test User");
        dto.CustomerEmail.Should().Be("test@example.com");
    }

    [Fact]
    public void ToListItemDto_StringifiesEnums()
    {
        var order = MakeOrder(OrderType.CustomBanner);
        var storage = MakeStorage();

        var dto = OrderMapper.ToListItemDto(order, null, storage);

        dto.Status.Should().Be("Paid");
        dto.OrderType.Should().Be("CustomBanner");
        dto.OrderState.Should().Be("Paid");
        dto.DeliveryType.Should().Be("Standard");
        dto.PackingMode.Should().Be("Folded");
    }

    [Fact]
    public void ToListItemDto_CustomBannerOrder_BuildsCustomBannerDetail()
    {
        var order = MakeOrder(OrderType.CustomBanner);
        var design = new BannerDesign { Id = 5, PreviewStoragePath = "banner-builder/1/preview.jpg" };
        order.Items.First().BannerDesign = design;
        var storage = MakeStorage();

        var dto = OrderMapper.ToListItemDto(order, null, storage);

        dto.CustomBanner.Should().NotBeNull();
        dto.CustomBanner!.PreviewUrl.Should().Contain("preview.jpg");
        dto.AiBanner.Should().BeNull();
        dto.ManualDesign.Should().BeNull();
    }

    [Fact]
    public void ToListItemDto_AiBannerOrder_WithDesignRequest_BuildsAiBannerDetail()
    {
        var order = MakeOrder(OrderType.AiBanner);
        var dr = new DesignRequest
        {
            Id = 20, AiPreviewPath = "design-requests/20/preview.png",
            ThemeDescription = "Bursdag", PersonName = "Ole", RevisionCount = 1
        };
        var storage = MakeStorage();

        var dto = OrderMapper.ToListItemDto(order, dr, storage);

        dto.AiBanner.Should().NotBeNull();
        dto.AiBanner!.PreviewUrl.Should().Contain("preview.png");
        dto.AiBanner.ThemeDescription.Should().Be("Bursdag");
        dto.AiBanner.PersonName.Should().Be("Ole");
        dto.AiBanner.DesignRequestId.Should().Be(20);
        dto.CustomBanner.Should().BeNull();
        dto.ManualDesign.Should().BeNull();
    }

    [Fact]
    public void ToListItemDto_AiBannerOrder_NullDesignRequest_ReturnsEmptyDetail()
    {
        var order = MakeOrder(OrderType.AiBanner);
        var storage = MakeStorage();

        var dto = OrderMapper.ToListItemDto(order, null, storage);

        dto.AiBanner.Should().NotBeNull();
        dto.AiBanner!.PreviewUrl.Should().BeNull();
    }

    [Fact]
    public void ToListItemDto_ManualDesignOrder_BuildsManualDesignDetail()
    {
        var order = MakeOrder(OrderType.ManualDesign);
        var dr = new DesignRequest
        {
            Id = 30, DesignerPreviewPath = "design-requests/30/designer.png",
            AspectRatio = "16:9", DesignerNotes = "See attached"
        };
        var storage = MakeStorage();

        var dto = OrderMapper.ToListItemDto(order, dr, storage);

        dto.ManualDesign.Should().NotBeNull();
        dto.ManualDesign!.PreviewUrl.Should().Contain("designer.png");
        dto.ManualDesign.AspectRatio.Should().Be("16:9");
        dto.ManualDesign.DesignerNotes.Should().Be("See attached");
        dto.ManualDesign.DesignRequestId.Should().Be(30);
        dto.AiBanner.Should().BeNull();
        dto.CustomBanner.Should().BeNull();
    }

    [Fact]
    public void ToListItemDto_ManualDesignOrder_NullDesignRequest_ReturnsEmptyDetail()
    {
        var order = MakeOrder(OrderType.ManualDesign);
        var storage = MakeStorage();

        var dto = OrderMapper.ToListItemDto(order, null, storage);

        dto.ManualDesign.Should().NotBeNull();
        dto.ManualDesign!.PreviewUrl.Should().BeNull();
    }

    [Fact]
    public void ToListItemDto_CreditPackOrder_ReturnsNullTypeDetails()
    {
        var order = MakeOrder(OrderType.CreditPack);
        var storage = MakeStorage();

        var dto = OrderMapper.ToListItemDto(order, null, storage);

        dto.CustomBanner.Should().BeNull();
        dto.AiBanner.Should().BeNull();
        dto.ManualDesign.Should().BeNull();
    }

    // ── ToDetailDto ───────────────────────────────────────────────────────────

    [Fact]
    public void ToDetailDto_MapsAllFields()
    {
        var order = MakeOrder();
        order.ShippingAddress = new Address
        {
            Line1 = "Test St 1", Line2 = null, PostalCode = "0001", City = "Oslo", Country = "NO"
        };
        order.ShipmentTracking = new ShipmentTracking
        {
            Carrier = "Bring", TrackingNumber = "12345", TrackingUrl = "https://x.com",
            ShippedAt = DateTime.UtcNow, EstimatedArrival = DateTime.UtcNow.AddDays(3)
        };
        var storage = MakeStorage();

        var dto = OrderMapper.ToDetailDto(order, null, storage);

        dto.Id.Should().Be(100);
        dto.UserId.Should().Be(1);
        dto.ShippingAddress.Should().NotBeNull();
        dto.ShippingAddress!.Line1.Should().Be("Test St 1");
        dto.ShipmentTracking.Should().NotBeNull();
        dto.ShipmentTracking!.Carrier.Should().Be("Bring");
        dto.Items.Should().HaveCount(1);
    }

    [Fact]
    public void ToDetailDto_ItemWithProductionStatus_MapsHistory()
    {
        var order = MakeOrder();
        order.Items.First().ProductionStatuses = new List<ProductionStatus>
        {
            new() { Id = 1, Stage = ProductionStage.Printing, UpdatedAt = DateTime.UtcNow, Notes = "Running" }
        };
        var storage = MakeStorage();

        var dto = OrderMapper.ToDetailDto(order, null, storage);

        dto.Items[0].CurrentProductionStage.Should().Be("Printing");
        dto.Items[0].ProductionStatusHistory.Should().HaveCount(1);
    }

    [Fact]
    public void ToDetailDto_ItemWithNoProductionStatus_DefaultsToQueued()
    {
        var order = MakeOrder();
        order.Items.First().ProductionStatuses = new List<ProductionStatus>();
        var storage = MakeStorage();

        var dto = OrderMapper.ToDetailDto(order, null, storage);

        dto.Items[0].CurrentProductionStage.Should().Be("Queued");
    }

    [Fact]
    public void ToDetailDto_NullShippingAddress_MapsToNull()
    {
        var order = MakeOrder();
        order.ShippingAddress = null;
        var storage = MakeStorage();

        var dto = OrderMapper.ToDetailDto(order, null, storage);

        dto.ShippingAddress.Should().BeNull();
    }

    [Fact]
    public void ToDetailDto_CustomBannerWithStoragePath_FallsBackToStoragePath()
    {
        var order = MakeOrder(OrderType.CustomBanner);
        // PreviewStoragePath is null → falls back to StoragePath
        var design = new BannerDesign { Id = 5, StoragePath = "banner-builder/1/file.jpg", PreviewStoragePath = null };
        order.Items.First().BannerDesign = design;
        var storage = MakeStorage();

        var dto = OrderMapper.ToDetailDto(order, null, storage);

        dto.CustomBanner.Should().NotBeNull();
        dto.CustomBanner!.PreviewUrl.Should().Contain("file.jpg");
    }

    [Fact]
    public void ToDetailDto_AiBannerFallsBackFromAiPreviewToFinalCropped()
    {
        var order = MakeOrder(OrderType.AiBanner);
        var dr = new DesignRequest
        {
            Id = 20, AiPreviewPath = null,
            FinalCroppedStoragePath = "design-requests/20/cropped.png"
        };
        var storage = MakeStorage();

        var dto = OrderMapper.ToDetailDto(order, dr, storage);

        dto.AiBanner!.PreviewUrl.Should().Contain("cropped.png");
    }

    [Fact]
    public void ToDetailDto_AiBannerFallsBackToAiResult()
    {
        var order = MakeOrder(OrderType.AiBanner);
        var dr = new DesignRequest
        {
            Id = 20, AiPreviewPath = null, FinalCroppedStoragePath = null,
            AiResultStoragePath = "design-requests/20/ai-result.png"
        };
        var storage = MakeStorage();

        var dto = OrderMapper.ToDetailDto(order, dr, storage);

        dto.AiBanner!.PreviewUrl.Should().Contain("ai-result.png");
    }

    [Fact]
    public void ToDetailDto_ManualDesignFallsBackFromDesignerPreviewToFinalCropped()
    {
        var order = MakeOrder(OrderType.ManualDesign);
        var dr = new DesignRequest
        {
            Id = 30, DesignerPreviewPath = null,
            FinalCroppedStoragePath = "design-requests/30/final.png"
        };
        var storage = MakeStorage();

        var dto = OrderMapper.ToDetailDto(order, dr, storage);

        dto.ManualDesign!.PreviewUrl.Should().Contain("final.png");
    }
}
