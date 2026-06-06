using BannerShop.Core.Enums;

namespace BannerShop.Core.Entities;

public class ProductionStatus
{
    public int Id { get; set; }
    public int OrderItemId { get; set; }
    public ProductionStage Stage { get; set; } = ProductionStage.Queued;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }

    // Navigation
    public OrderItem OrderItem { get; set; } = null!;
}
