namespace BannerShop.Api.Models.Catalog;

/// <summary>
/// Returned by <c>GET /api/sizes/price</c> — the cheapest matching pricing rule plus
/// the price it produces for the requested (width, height [, material]) banner.
/// BANNERSH-255.
/// </summary>
public class PriceResponseDto
{
    /// <summary>ID of the matching <see cref="BannerShop.Core.Entities.BannerSize"/> rule.</summary>
    public int SizeId { get; set; }
    public int WidthCm { get; set; }
    public int HeightCm { get; set; }
    /// <summary>Material ID that won the cheapest match (useful when caller didn't pin one).</summary>
    public int MaterialId { get; set; }
    public decimal PriceNok { get; set; }
}
