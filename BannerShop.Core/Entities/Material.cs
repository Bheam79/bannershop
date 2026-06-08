namespace BannerShop.Core.Entities;

public class Material
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int WidthCm { get; set; }
    public int WeightGsm { get; set; }
    public decimal PricePerSqm { get; set; }
    public DateTime? AvailableFrom { get; set; }

    /// <summary>
    /// Maximum banner width (in cm) that can be produced from this material as a single
    /// piece, i.e. without gluing multiple panels together. Banners wider than this trigger
    /// a panel-count multiplier in <c>PricingService</c> (see <c>banner_panel_overlap_cm</c>
    /// pricing parameter for the overlap between panels).
    /// Defaults to the material roll <see cref="WidthCm"/>.
    /// </summary>
    public int MaxBannerWidthCm { get; set; }

    // Navigation
    public ICollection<BannerSize> BannerSizes { get; set; } = new List<BannerSize>();
}
