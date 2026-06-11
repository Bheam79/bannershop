using BannerShop.Api.Services.Email;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Unit tests for the static OrderEmailTemplates HTML builders.
/// All methods are pure / side-effect-free, so tests need no mocks or DB.
/// </summary>
public class OrderEmailTemplatesTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Order MakeOrder(int id = 42, DeliveryType deliveryType = DeliveryType.Standard)
    {
        var user = new User { Id = 1, Name = "Ola Nordmann", Email = "ola@example.com", PasswordHash = "x", Role = UserRole.Customer };
        var mat  = new Material { Id = 1, Name = "400g Frontlit", WidthCm = 160, MaxBannerWidthCm = 160, WeightGsm = 400, PricePerSqm = 180m };
        var size = new BannerSize { Id = 1, Name = "300 × 150 cm", WidthCm = 300, HeightCm = 150, IsActive = true, MaterialId = 1, Material = mat };
        var item = new OrderItem
        {
            Id = 1,
            BannerSizeId = 1,
            BannerSize = size,
            HeightCm = 150,
            Quantity = 2,
            AreaSqm = 9m,
            UnitPriceNok = 540m,
            EyeletOption = EyeletOption.None,
            EyeletCount = 0,
            EyeletFeeNok = 0m,
            LineTotalNok = 1080m
        };
        return new Order
        {
            Id = id,
            User = user,
            UserId = user.Id,
            Status = OrderStatus.Paid,
            DeliveryType = deliveryType,
            ShippingCostNok = deliveryType == DeliveryType.Standard ? 199m : 0m,
            ExpressFeeNok = 0m,
            AiActivationFeeNok = 0m,
            TotalNok = deliveryType == DeliveryType.Standard ? 1279m : 1080m,
            EstimatedDelivery = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            Items = new List<OrderItem> { item },
            CreatedAt = DateTime.UtcNow
        };
    }

    // ── OrderConfirmation ─────────────────────────────────────────────────────

    [Fact]
    public void BuildOrderConfirmationHtml_ContainsOrderId()
    {
        var order = MakeOrder(id: 42);

        var html = OrderEmailTemplates.BuildOrderConfirmationHtml(order);

        html.Should().Contain("#42");
    }

    [Fact]
    public void BuildOrderConfirmationHtml_ContainsCustomerName()
    {
        var order = MakeOrder();

        var html = OrderEmailTemplates.BuildOrderConfirmationHtml(order);

        html.Should().Contain("Ola Nordmann");
    }

    [Fact]
    public void BuildOrderConfirmationHtml_ContainsItemDetails()
    {
        var order = MakeOrder();

        var html = OrderEmailTemplates.BuildOrderConfirmationHtml(order);

        // The size name is HTML-encoded by WebUtility.HtmlEncode (× → &#215;)
        html.Should().Contain("300 &#215; 150 cm");
        // Norwegian nb-NO uses NO-BREAK SPACE (U+00A0) as the thousands separator
        html.Should().Contain("1 080");  // 1 080,00 kr
    }

    [Fact]
    public void BuildOrderConfirmationHtml_StandardDelivery_ContainsShipping()
    {
        var order = MakeOrder(deliveryType: DeliveryType.Standard);

        var html = OrderEmailTemplates.BuildOrderConfirmationHtml(order);

        html.Should().Contain("Frakt");
        html.Should().Contain("199");
    }

    [Fact]
    public void BuildOrderConfirmationHtml_Pickup_ContainsPickupAddress()
    {
        var order = MakeOrder(deliveryType: DeliveryType.Pickup);

        var html = OrderEmailTemplates.BuildOrderConfirmationHtml(order);

        html.Should().Contain("Rigedalen 43");
        html.Should().Contain("Henting (gratis)");
    }

    [Fact]
    public void BuildOrderConfirmationHtml_ContainsEstimatedDelivery()
    {
        var order = MakeOrder();
        order.EstimatedDelivery = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);

        var html = OrderEmailTemplates.BuildOrderConfirmationHtml(order);

        html.Should().Contain("juli 2026");
    }

    [Fact]
    public void BuildOrderConfirmationHtml_NullEstimatedDelivery_ShowsNotSet()
    {
        var order = MakeOrder();
        order.EstimatedDelivery = null;

        var html = OrderEmailTemplates.BuildOrderConfirmationHtml(order);

        html.Should().Contain("ikke fastsatt");
    }

    [Fact]
    public void BuildOrderConfirmationHtml_NullUserName_UsesDefaultGreeting()
    {
        var order = MakeOrder();
        order.User!.Name = null!;

        var html = OrderEmailTemplates.BuildOrderConfirmationHtml(order);

        html.Should().Contain("Hei kunde");
    }

    [Fact]
    public void BuildOrderConfirmationHtml_WithExpressFee_ContainsExpressFee()
    {
        var order = MakeOrder();
        order.ExpressFeeNok = 500m;
        order.TotalNok += 500m;

        var html = OrderEmailTemplates.BuildOrderConfirmationHtml(order);

        html.Should().Contain("Ekspressgebyr");
    }

    [Fact]
    public void BuildOrderConfirmationHtml_WithAiActivationFee_ContainsAiFee()
    {
        var order = MakeOrder();
        order.AiActivationFeeNok = 95m;
        order.TotalNok += 95m;

        var html = OrderEmailTemplates.BuildOrderConfirmationHtml(order);

        html.Should().Contain("AI aktivering");
    }

    [Fact]
    public void BuildOrderConfirmationHtml_ItemWithNullBannerSize_UsesFallback()
    {
        var order = MakeOrder();
        order.Items.First().BannerSize = null;
        order.Items.First().BannerSizeId = 99;

        var html = OrderEmailTemplates.BuildOrderConfirmationHtml(order);

        // The fallback string is HTML-encoded by WebUtility.HtmlEncode (ø → &#248;)
        html.Should().Contain("Bannerst&#248;rrelse 99");
    }

    [Fact]
    public void BuildOrderConfirmationHtml_ItemWithCustomWidth_ShowsCustomDimensions()
    {
        var order = MakeOrder();
        order.Items.First().CustomWidthCm = 250;

        var html = OrderEmailTemplates.BuildOrderConfirmationHtml(order);

        html.Should().Contain("250×150 cm");
    }

    [Fact]
    public void BuildOrderConfirmationHtml_ItemNullWidth_ShowsHeightOnly()
    {
        var order = MakeOrder();
        order.Items.First().BannerSize!.WidthCm = null;
        order.Items.First().CustomWidthCm = null;

        var html = OrderEmailTemplates.BuildOrderConfirmationHtml(order);

        html.Should().Contain("150 cm høyde");
    }

    // ── ProductionStarted ────────────────────────────────────────────────────

    [Fact]
    public void BuildProductionStartedHtml_ContainsOrderId()
    {
        var order = MakeOrder(id: 77);

        var html = OrderEmailTemplates.BuildProductionStartedHtml(order);

        html.Should().Contain("#77");
    }

    [Fact]
    public void BuildProductionStartedHtml_ContainsCustomerName()
    {
        var order = MakeOrder();

        var html = OrderEmailTemplates.BuildProductionStartedHtml(order);

        html.Should().Contain("Ola Nordmann");
    }

    [Fact]
    public void BuildProductionStartedHtml_ContainsEstimatedDelivery()
    {
        var order = MakeOrder();
        order.EstimatedDelivery = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);

        var html = OrderEmailTemplates.BuildProductionStartedHtml(order);

        html.Should().Contain("august 2026");
    }

    [Fact]
    public void BuildProductionStartedHtml_NullEstimatedDelivery_ShowsNotSet()
    {
        var order = MakeOrder();
        order.EstimatedDelivery = null;

        var html = OrderEmailTemplates.BuildProductionStartedHtml(order);

        html.Should().Contain("ikke fastsatt");
    }

    [Fact]
    public void BuildProductionStartedHtml_NullUser_UsesDefaultGreeting()
    {
        var order = MakeOrder();
        order.User = null;

        var html = OrderEmailTemplates.BuildProductionStartedHtml(order);

        html.Should().Contain("Hei kunde");
    }

    // ── ShipmentDispatched ────────────────────────────────────────────────────

    [Fact]
    public void BuildShipmentDispatchedHtml_ContainsTrackingInfo()
    {
        var order = MakeOrder(id: 55);
        order.ShipmentTracking = new ShipmentTracking
        {
            Carrier = "Bring",
            TrackingNumber = "987654321",
            TrackingUrl = "https://tracking.bring.com/987654321",
            ShippedAt = DateTime.UtcNow,
            EstimatedArrival = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc)
        };

        var html = OrderEmailTemplates.BuildShipmentDispatchedHtml(order);

        html.Should().Contain("Bring");
        html.Should().Contain("987654321");
        html.Should().Contain("https://tracking.bring.com/987654321");
        html.Should().Contain("#55");
    }

    [Fact]
    public void BuildShipmentDispatchedHtml_NullTrackingUrl_OmitsTrackingLink()
    {
        var order = MakeOrder();
        order.ShipmentTracking = new ShipmentTracking
        {
            Carrier = "PostNord",
            TrackingNumber = "111",
            TrackingUrl = null
        };

        var html = OrderEmailTemplates.BuildShipmentDispatchedHtml(order);

        html.Should().Contain("PostNord");
        html.Should().NotContain("Følg pakken");
    }

    [Fact]
    public void BuildShipmentDispatchedHtml_WithEstimatedArrival_ShowsArrivalDate()
    {
        var order = MakeOrder();
        order.ShipmentTracking = new ShipmentTracking
        {
            Carrier = "Bring",
            TrackingNumber = "123",
            EstimatedArrival = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc)
        };

        var html = OrderEmailTemplates.BuildShipmentDispatchedHtml(order);

        html.Should().Contain("september 2026");
    }

    [Fact]
    public void BuildShipmentDispatchedHtml_FallsBackToOrderEstimatedDelivery()
    {
        var order = MakeOrder();
        order.EstimatedDelivery = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
        order.ShipmentTracking = new ShipmentTracking
        {
            Carrier = "Bring",
            TrackingNumber = "123",
            EstimatedArrival = null  // no arrival on tracking
        };

        var html = OrderEmailTemplates.BuildShipmentDispatchedHtml(order);

        html.Should().Contain("oktober 2026");
    }

    [Fact]
    public void BuildShipmentDispatchedHtml_NoArrivalNoDelivery_ShowsNotSet()
    {
        var order = MakeOrder();
        order.EstimatedDelivery = null;
        order.ShipmentTracking = new ShipmentTracking
        {
            Carrier = "Bring",
            TrackingNumber = "123",
            EstimatedArrival = null
        };

        var html = OrderEmailTemplates.BuildShipmentDispatchedHtml(order);

        html.Should().Contain("ikke fastsatt");
    }
}
