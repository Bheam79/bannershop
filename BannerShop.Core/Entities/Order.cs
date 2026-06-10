using BannerShop.Core.Enums;

namespace BannerShop.Core.Entities;

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Draft;

    /// <summary>Classifies the fulfilment flow (custom-upload / AI / manual-design).</summary>
    public OrderType OrderType { get; set; } = OrderType.CustomBanner;

    /// <summary>
    /// Lifecycle state — a richer superset of the legacy <see cref="Status"/> field.
    /// Use <see cref="BannerShop.Core.Helpers.OrderStateHelper.ValidSequence"/> to
    /// enumerate the states applicable for this order's <see cref="OrderType"/>.
    /// </summary>
    public OrderState OrderState { get; set; } = OrderState.Draft;

    public DeliveryType DeliveryType { get; set; } = DeliveryType.Standard;

    /// <summary>
    /// How the customer chose to pack the banner for shipping. Recorded at order-draft
    /// time so the fulfilment team knows which packaging method the customer paid for
    /// (BANNERSH-149). Defaults to <see cref="PackingMode.Rolled"/> (historical default).
    /// </summary>
    public PackingMode PackingMode { get; set; } = PackingMode.Rolled;

    public int? ShippingAddressId { get; set; }
    public decimal ShippingCostNok { get; set; }
    public decimal ExpressFeeNok { get; set; }
    /// <summary>
    /// Per-order AI activation fee (95 kr) charged when any order item is linked to an
    /// AI-generated design (DesignRequestId != null). On payment, 20 AI credits are
    /// granted to the user (BANNERSH-68).
    /// </summary>
    public decimal AiActivationFeeNok { get; set; }
    public decimal TotalNok { get; set; }
    public string? StripePaymentIntentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EstimatedDelivery { get; set; }

    /// <summary>
    /// BANNERSH-185: soft-delete flag. When <c>true</c>, the order is hidden from all
    /// customer- and admin-facing listings and lookups. Set via the customer's "Slett"
    /// button on Draft / PendingPayment orders (orders that never made it to a paid /
    /// production state). Paid orders are not deletable.
    /// </summary>
    public bool Deleted { get; set; }

    // Navigation
    public User User { get; set; } = null!;
    public Address? ShippingAddress { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ShipmentTracking? ShipmentTracking { get; set; }
}
