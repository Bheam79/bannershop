namespace BannerShop.Api.Models.Catalog;

public class MaterialDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int WidthCm { get; set; }
    /// <summary>
    /// Max banner width (in cm) producible as a single piece without gluing multiple
    /// panels together. Beyond this width, <c>PricingService</c> applies a per-panel
    /// price multiplier (BANNERSH-88).
    /// </summary>
    public int MaxBannerWidthCm { get; set; }
    public int WeightGsm { get; set; }
    public decimal PricePerSqm { get; set; }
    public DateTime? AvailableFrom { get; set; }
}
