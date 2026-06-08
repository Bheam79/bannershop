using System.ComponentModel.DataAnnotations;
using BannerShop.Core.Enums;

namespace BannerShop.Api.Models.Orders;

// ── Request DTOs ─────────────────────────────────────────────────────────────

public class CreateOrderDraftRequest
{
    [Required]
    public DeliveryType DeliveryType { get; set; } = DeliveryType.Standard;

    [Required]
    public AddressInputDto ShippingAddress { get; set; } = new();

    [Required, MinLength(1)]
    public List<OrderItemInputDto> Items { get; set; } = new();
}

public class AddressInputDto
{
    [Required, StringLength(200)]
    public string Line1 { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Line2 { get; set; }

    [Required, StringLength(20, MinimumLength = 4)]
    public string PostalCode { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string City { get; set; } = string.Empty;

    [StringLength(10)]
    public string Country { get; set; } = "NO";
}

public class OrderItemInputDto
{
    [Range(1, int.MaxValue)]
    public int BannerSizeId { get; set; }

    [Range(50, 1000)]
    public int? CustomWidthCm { get; set; }

    [Range(1, 100)]
    public int Quantity { get; set; } = 1;

    [StringLength(1000)]
    public string? Notes { get; set; }

    [Range(1, int.MaxValue)]
    public int? BannerDesignId { get; set; }

    /// <summary>
    /// Optional FK to a DesignRequest for AI-generated banners. When set, the order creation
    /// endpoint validates that the request belongs to the caller and adds the mandatory
    /// AI activation fee (BANNERSH-68).
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? DesignRequestId { get; set; }
}

public class UpdateOrderStatusRequest
{
    [Required]
    public OrderStatus Status { get; set; }
}

public class UpdateProductionRequest
{
    [Required]
    public ProductionStage Stage { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }
}

public class SetShippingRequest
{
    [Required, StringLength(100)]
    public string Carrier { get; set; } = "Bring";

    [Required, StringLength(200)]
    public string TrackingNumber { get; set; } = string.Empty;

    [StringLength(500)]
    public string? TrackingUrl { get; set; }

    public DateTime? ShippedAt { get; set; }
    public DateTime? EstimatedArrival { get; set; }
}

// ── Response DTOs ────────────────────────────────────────────────────────────

public class CreateOrderDraftResponseDto
{
    public int OrderId { get; set; }
    public string ClientSecret { get; set; } = string.Empty;
    public decimal TotalNok { get; set; }
    public OrderPriceBreakdownDto Breakdown { get; set; } = new();
}

public class OrderPriceBreakdownDto
{
    public decimal ItemsSubtotalNok    { get; set; }
    public decimal ShippingCostNok     { get; set; }
    public decimal ExpressFeeNok       { get; set; }
    public decimal AiActivationFeeNok  { get; set; }
    public decimal TotalNok            { get; set; }
}

public class OrderListItemDto
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string DeliveryType { get; set; } = string.Empty;
    public decimal TotalNok { get; set; }
    public int ItemCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EstimatedDelivery { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
}

public class OrderDetailDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string Status { get; set; } = string.Empty;
    public string DeliveryType { get; set; } = string.Empty;
    public decimal ShippingCostNok { get; set; }
    public decimal ExpressFeeNok { get; set; }
    public decimal AiActivationFeeNok { get; set; }
    public decimal TotalNok { get; set; }
    public string? StripePaymentIntentId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? EstimatedDelivery { get; set; }
    public OrderAddressDto? ShippingAddress { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public ShipmentTrackingDto? ShipmentTracking { get; set; }
}

public class OrderAddressDto
{
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string PostalCode { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = "NO";
}

public class OrderItemDto
{
    public int Id { get; set; }
    public int? BannerSizeId { get; set; }
    public string? BannerSizeName { get; set; }
    public int? CustomWidthCm { get; set; }
    public int HeightCm { get; set; }
    public int Quantity { get; set; }
    public decimal AreaSqm { get; set; }
    public decimal UnitPriceNok { get; set; }
    public decimal LineTotalNok { get; set; }
    public string? Notes { get; set; }
    public int? BannerDesignId { get; set; }
    public int? DesignRequestId { get; set; }
    public string CurrentProductionStage { get; set; } = "Queued";
    public List<ProductionStatusDto> ProductionStatusHistory { get; set; } = new();
}

public class ProductionStatusDto
{
    public int Id { get; set; }
    public string Stage { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public string? Notes { get; set; }
}

public class ShipmentTrackingDto
{
    public string Carrier { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string? TrackingUrl { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? EstimatedArrival { get; set; }
    public DateTime? DeliveredAt { get; set; }
}
