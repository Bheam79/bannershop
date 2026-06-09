using System.ComponentModel.DataAnnotations;
using BannerShop.Core.Enums;
using BannerShop.Core.Helpers;

namespace BannerShop.Api.Models.Orders;

// ── Request DTOs ─────────────────────────────────────────────────────────────

public class CreateOrderDraftRequest
{
    [Required]
    public DeliveryType DeliveryType { get; set; } = DeliveryType.Standard;

    /// <summary>
    /// Required for Standard and Express delivery. Omit (or set to null) for Pickup orders.
    /// </summary>
    public AddressInputDto? ShippingAddress { get; set; }

    /// <summary>
    /// How the customer wants the order packaged. Affects the Bring quote computed
    /// server-side at order draft time so the persisted shipping cost matches the
    /// price the customer saw in the cart (BANNERSH-143). Defaults to
    /// <see cref="PackingMode.Rolled"/>.
    /// </summary>
    public PackingMode PackingMode { get; set; } = PackingMode.Rolled;

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

    /// <summary>
    /// Eyelet (malje) finishing option. Hem is not possible on PVC banners.
    /// Defaults to <see cref="EyeletOption.None"/> (no eyelets).
    /// </summary>
    public EyeletOption EyeletOption { get; set; } = EyeletOption.None;
}

public class UpdateOrderStatusRequest
{
    [Required]
    public OrderStatus Status { get; set; }
}

/// <summary>Request body for <c>POST /api/admin/orders/{id}/advance</c>.</summary>
public class AdvanceOrderStateRequest
{
    [Required]
    public OrderState Next { get; set; }
}

/// <summary>
/// Request body for <c>POST /api/admin/orders/{id}/advance-state</c>.
/// Uses <c>toState</c> as the property name for consistency with the unified orders API design.
/// </summary>
public class AdvanceOrderStateByToStateRequest
{
    [Required]
    public OrderState ToState { get; set; }
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

// ── Type-specific order detail sub-objects ───────────────────────────────────

/// <summary>
/// Type-specific details for a <see cref="BannerShop.Core.Enums.OrderType.CustomBanner"/> order.
/// Populated from the first order item's BannerSize + Material.
/// </summary>
public class CustomBannerDetailDto
{
    /// <summary>Public URL of the uploaded design preview (if any).</summary>
    public string? PreviewUrl { get; set; }
    /// <summary>Display name of the selected banner size.</summary>
    public string? BannerSizeName { get; set; }
    /// <summary>Display name of the banner material.</summary>
    public string? MaterialName { get; set; }
}

/// <summary>
/// Type-specific details for an <see cref="BannerShop.Core.Enums.OrderType.AiBanner"/> order.
/// Populated from the linked <c>DesignRequest</c>.
/// </summary>
public class AiBannerDetailDto
{
    /// <summary>Customer-visible preview URL of the AI-generated banner (low-res when available).</summary>
    public string? PreviewUrl { get; set; }
    /// <summary>Free-text theme / style description the customer entered.</summary>
    public string? ThemeDescription { get; set; }
    /// <summary>Name of the person the banner is for.</summary>
    public string? PersonName { get; set; }
    /// <summary>Number of AI re-generations the customer has requested so far.</summary>
    public int RevisionCount { get; set; }
    /// <summary>ID of the linked DesignRequest (if any). Used by admin to navigate to design detail.</summary>
    public int? DesignRequestId { get; set; }
}

/// <summary>
/// Type-specific details for a <see cref="BannerShop.Core.Enums.OrderType.ManualDesign"/> order.
/// Populated from the linked <c>DesignRequest</c>.
/// </summary>
public class ManualDesignDetailDto
{
    /// <summary>Public URL of the designer's uploaded preview (if any).</summary>
    public string? PreviewUrl { get; set; }
    /// <summary>Customer-selected aspect ratio ("16:9" or "18:9").</summary>
    public string? AspectRatio { get; set; }
    /// <summary>Internal notes from the designer / admin.</summary>
    public string? DesignerNotes { get; set; }
    /// <summary>ID of the linked DesignRequest. Used by admin to upload the finished design.</summary>
    public int? DesignRequestId { get; set; }
}

public class OrderListItemDto
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string OrderType { get; set; } = string.Empty;
    public string OrderState { get; set; } = string.Empty;
    public string DeliveryType { get; set; } = string.Empty;
    /// <summary>How the banner is packed for shipping (Rolled / Folded). Recorded at order time (BANNERSH-149).</summary>
    public string PackingMode { get; set; } = "Rolled";
    public decimal TotalNok { get; set; }
    public int ItemCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EstimatedDelivery { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }

    // ── Type-specific sub-objects ─────────────────────────────────────────────
    /// <summary>Populated only when <see cref="OrderType"/> is "CustomBanner".</summary>
    public CustomBannerDetailDto? CustomBanner { get; set; }
    /// <summary>Populated only when <see cref="OrderType"/> is "AiBanner".</summary>
    public AiBannerDetailDto? AiBanner { get; set; }
    /// <summary>Populated only when <see cref="OrderType"/> is "ManualDesign".</summary>
    public ManualDesignDetailDto? ManualDesign { get; set; }
}

public class OrderDetailDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string Status { get; set; } = string.Empty;
    /// <summary>Fulfilment flow: CustomBanner / AiBanner / ManualDesign.</summary>
    public string OrderType { get; set; } = string.Empty;
    /// <summary>Lifecycle state per the state-machine (BANNERSH-109).</summary>
    public string OrderState { get; set; } = string.Empty;
    public string DeliveryType { get; set; } = string.Empty;
    /// <summary>How the banner is packed for shipping (Rolled / Folded). Recorded at order time (BANNERSH-149).</summary>
    public string PackingMode { get; set; } = "Rolled";
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

    // ── Type-specific sub-objects ─────────────────────────────────────────────
    /// <summary>Populated only when <see cref="OrderType"/> is "CustomBanner".</summary>
    public CustomBannerDetailDto? CustomBanner { get; set; }
    /// <summary>Populated only when <see cref="OrderType"/> is "AiBanner".</summary>
    public AiBannerDetailDto? AiBanner { get; set; }
    /// <summary>Populated only when <see cref="OrderType"/> is "ManualDesign".</summary>
    public ManualDesignDetailDto? ManualDesign { get; set; }
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
    /// <summary>Base banner price per unit (excluding eyelets).</summary>
    public decimal UnitPriceNok { get; set; }
    /// <summary>Eyelet option chosen at order time ("None", "FourCorners", "PerMeter").</summary>
    public string EyeletOption { get; set; } = "None";
    /// <summary>Number of eyelets on one banner (0 when EyeletOption is None).</summary>
    public int EyeletCount { get; set; }
    /// <summary>Eyelet fee per unit (EyeletCount × price_per_eyelet at order time).</summary>
    public decimal EyeletFeeNok { get; set; }
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
