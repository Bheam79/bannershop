namespace BannerShop.Core.Entities;

public class ShipmentTracking
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string Carrier { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string? TrackingUrl { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? EstimatedArrival { get; set; }
    public DateTime? DeliveredAt { get; set; }

    // Navigation
    public Order Order { get; set; } = null!;
}
