namespace BannerShop.Core.Entities;

public class BannerSize
{
    public int Id { get; set; }
    public int? WidthCm { get; set; }
    public int HeightCm { get; set; }
    public bool IsCustomWidth { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int MaterialId { get; set; }
    public decimal? FixedPrice { get; set; }
    public int SortOrder { get; set; }

    // Navigation
    public Material Material { get; set; } = null!;
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
