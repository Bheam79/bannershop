using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Models.Orders;
using BannerShop.Api.Services.Orders;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Unit tests for the per-OrderItem fields produced by <see cref="OrderMapper"/>.MapItem
/// (reached via ToDetailDto -> Items). These are the fields the order-detail UI now
/// actually renders per banner row (BANNERSH-249): the DesignSource badge, per-item
/// preview / download URLs, and the bundled Manual designer fee. The legacy
/// order-level type-specific blocks (CustomBanner / AiBanner / ManualDesign) covered by
/// OrderMapperTests are back-compat only and no longer drive the UI, so this file fills
/// the gap where none of those per-item fields were asserted anywhere.
///
/// Pure mapping logic - no DB, no HTTP.
/// </summary>
public class OrderMapperItemTests
{
    private static BannerFileStorage MakeStorage()
    {
        var opts = Options.Create(new FileStorageOptions
        {
            LocalRoot      = "/tmp/test-storage",
            PublicBaseUrl  = "https://example.com/uploads",
            MaxUploadBytes = 50L * 1024 * 1024
        });
        return new BannerFileStorage(opts);
    }

    /// <summary>
    /// Minimal Order carrying a single OrderItem; callers attach a BannerDesign
    /// and/or DesignRequest to the item to exercise the MapItem branches.
    /// </summary>
    private static Order MakeOrderWithItem(OrderType type = OrderType.CustomBanner)
    {
        var user = new User { Id = 1, Name = "Test User", Email = "test@example.com", PasswordHash = "x", Role = UserRole.Customer };
        var item = new OrderItem
        {
            Id = 10, CustomWidthCm = 300, HeightCm = 150, Quantity = 1, AreaSqm = 4.5m,
            UnitPriceNok = 540m, EyeletOption = EyeletOption.None,
            EyeletCount = 0, EyeletFeeNok = 0m, LineTotalNok = 540m
        };

        return new Order
        {
            Id = 100,
            User = user, UserId = user.Id,
            Status = OrderStatus.Paid,
            OrderType = type,
            OrderState = OrderState.Paid,
            DeliveryType = DeliveryType.Standard,
            PackingMode = PackingMode.Folded,
            ShippingCostNok = 199m, ExpressFeeNok = 0m, AiActivationFeeNok = 0m,
            TotalNok = 739m,
            CreatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Items = new List<OrderItem> { item }
        };
    }

    private static OrderItemDto MapSingleItem(Order order) =>
        OrderMapper.ToDetailDto(order, null, MakeStorage()).Items.Single();

    [Fact]
    public void CustomUpload_SetsSourceAndUsesBannerDesignPreview()
    {
        var order = MakeOrderWithItem(OrderType.CustomBanner);
        order.Items.First().BannerDesign = new BannerDesign
        {
            Id = 5,
            PreviewStoragePath = "banner-builder/1/preview.jpg",
            StoragePath        = "banner-builder/1/full.jpg"
        };

        var item = MapSingleItem(order);

        item.DesignSource.Should().Be("CustomUpload");
        item.DesignPreviewUrl.Should().Contain("preview.jpg");
        // Download always points at the full-res original, not the preview.
        item.DesignDownloadUrl.Should().Contain("full.jpg");
        item.ManualDesignFeeNok.Should().Be(0m);
    }

    [Fact]
    public void CustomUpload_NullPreview_FallsBackToStoragePathForPreview()
    {
        var order = MakeOrderWithItem(OrderType.CustomBanner);
        order.Items.First().BannerDesign = new BannerDesign
        {
            Id = 5, PreviewStoragePath = null, StoragePath = "banner-builder/1/full.jpg"
        };

        var item = MapSingleItem(order);

        item.DesignPreviewUrl.Should().Contain("full.jpg");
        item.DesignDownloadUrl.Should().Contain("full.jpg");
    }

    [Fact]
    public void AiDesignRequest_SetsAiSourcePreviewAndCroppedDownload()
    {
        var order = MakeOrderWithItem(OrderType.AiBanner);
        order.Items.First().DesignRequest = new DesignRequest
        {
            Id = 20, Mode = DesignRequestMode.Ai,
            AiPreviewPath           = "design-requests/20/preview.png",
            FinalCroppedStoragePath = "design-requests/20/cropped.png"
        };

        var item = MapSingleItem(order);

        item.DesignSource.Should().Be("Ai");
        item.DesignPreviewUrl.Should().Contain("preview.png");
        // Download prefers the full-res cropped file over the preview.
        item.DesignDownloadUrl.Should().Contain("cropped.png");
        item.ManualDesignFeeNok.Should().Be(0m);
    }

    [Fact]
    public void AiDesignRequest_PreviewFallsThroughToAiResult()
    {
        var order = MakeOrderWithItem(OrderType.AiBanner);
        order.Items.First().DesignRequest = new DesignRequest
        {
            Id = 20, Mode = DesignRequestMode.Ai,
            AiPreviewPath = null, DesignerPreviewPath = null, FinalCroppedStoragePath = null,
            AiResultStoragePath = "design-requests/20/ai-result.png"
        };

        var item = MapSingleItem(order);

        item.DesignPreviewUrl.Should().Contain("ai-result.png");
        item.DesignDownloadUrl.Should().Contain("ai-result.png");
    }

    [Fact]
    public void ManualDesignRequest_SetsManualSourceAndBundlesDesignerFee()
    {
        var order = MakeOrderWithItem(OrderType.ManualDesign);
        order.Items.First().DesignRequest = new DesignRequest
        {
            Id = 30, Mode = DesignRequestMode.Manual, PriceNok = 495m,
            DesignerPreviewPath = "design-requests/30/designer.png"
        };

        var item = MapSingleItem(order);

        item.DesignSource.Should().Be("Manual");
        item.DesignPreviewUrl.Should().Contain("designer.png");
        // The 495 kr designer fee is exposed per-item for the "Designhonorar" row.
        item.ManualDesignFeeNok.Should().Be(495m);
    }

    [Fact]
    public void PrefersBannerDesignPreviewWhenBothDesignsPresent()
    {
        var order = MakeOrderWithItem(OrderType.CustomBanner);
        order.Items.First().BannerDesign = new BannerDesign
        {
            Id = 5, PreviewStoragePath = "banner-builder/1/upload.jpg", StoragePath = "banner-builder/1/upload.jpg"
        };
        order.Items.First().DesignRequest = new DesignRequest
        {
            Id = 30, Mode = DesignRequestMode.Manual, PriceNok = 495m,
            DesignerPreviewPath = "design-requests/30/designer.png"
        };

        var item = MapSingleItem(order);

        // BannerDesign is assigned first and wins the preview slot (?? short-circuits),
        // but the DesignRequest still runs second and stamps source + designer fee.
        item.DesignPreviewUrl.Should().Contain("upload.jpg");
        item.DesignSource.Should().Be("Manual");
        item.ManualDesignFeeNok.Should().Be(495m);
    }

    [Fact]
    public void NoDesign_SourceNoneAndNullUrls()
    {
        var order = MakeOrderWithItem(OrderType.CustomBanner);

        var item = MapSingleItem(order);

        item.DesignSource.Should().Be("None");
        item.DesignPreviewUrl.Should().BeNull();
        item.DesignDownloadUrl.Should().BeNull();
        item.ManualDesignFeeNok.Should().Be(0m);
    }
}
