namespace BannerShop.Api.Models.Catalog;

public class MaterialDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int WidthCm { get; set; }
    public int WeightGsm { get; set; }
    public decimal PricePerSqm { get; set; }
    public DateTime? AvailableFrom { get; set; }
}
