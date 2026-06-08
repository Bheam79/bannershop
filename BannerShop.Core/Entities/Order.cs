using BannerShop.Core.Enums;

namespace BannerShop.Core.Entities;

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Draft;
    public DeliveryType DeliveryType { get; set; } = DeliveryType.Standard;
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

    // Navigation
    public User User { get; set; } = null!;
    public Address? ShippingAddress { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ShipmentTracking? ShipmentTracking { get; set; }
}
