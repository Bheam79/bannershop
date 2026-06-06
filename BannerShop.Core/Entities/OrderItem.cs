namespace BannerShop.Core.Entities;

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int? BannerSizeId { get; set; }
    public int? CustomWidthCm { get; set; }
    public int HeightCm { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal AreaSqm { get; set; }
    public decimal UnitPriceNok { get; set; }
    public decimal LineTotalNok { get; set; }
    public string? Notes { get; set; }
    public int? BannerDesignId { get; set; }

    // Navigation
    public Order Order { get; set; } = null!;
    public BannerSize? BannerSize { get; set; }
    public BannerDesign? BannerDesign { get; set; }
    public ICollection<ProductionStatus> ProductionStatuses { get; set; } = new List<ProductionStatus>();
}
