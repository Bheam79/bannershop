namespace BannerShop.Api.Models.Catalog;

/// <summary>
/// Banner pricing rule exposed to admin + customer-facing endpoints. The dimensions
/// now describe a RANGE (min/max width × min/max height) plus a pricing formula —
/// see <see cref="BannerShop.Core.Entities.BannerSize"/> for the model and BANNERSH-255
/// for the rewrite history.
/// </summary>
public class BannerSizeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int MaterialId { get; set; }
    public MaterialDto Material { get; set; } = null!;
    public int SortOrder { get; set; }

    // Range covered by this rule
    public int MinWidthCm { get; set; }
    public int MaxWidthCm { get; set; }
    public int MinHeightCm { get; set; }
    public int MaxHeightCm { get; set; }

    // Pricing formula
    public int PricingHeightCm { get; set; }
    public int PricingMultiplier { get; set; }
    public decimal? FixedPrice { get; set; }

    /// <summary>
    /// Computed sample price for the lower-left corner of the range
    /// (<see cref="MinWidthCm"/> × <see cref="MinHeightCm"/>). Useful for the admin
    /// table to give a sanity check of the formula output.
    /// </summary>
    public decimal CalculatedPrice { get; set; }

    /// <summary>Availability date derived from the material (null = available now).</summary>
    public DateTime? AvailableFrom { get; set; }
}
