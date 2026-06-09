namespace BannerShop.Api.Models.Catalog;

public class BannerSizeDto
{
    public int Id { get; set; }
    public int? WidthCm { get; set; }
    public int HeightCm { get; set; }
    public bool IsCustomWidth { get; set; }
    public bool IsCustomHeight { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int MaterialId { get; set; }
    public MaterialDto Material { get; set; } = null!;
    public decimal? FixedPrice { get; set; }
    public int SortOrder { get; set; }
    public decimal CalculatedPrice { get; set; }
    /// <summary>Availability date derived from the material (null = available now).</summary>
    public DateTime? AvailableFrom { get; set; }
}
