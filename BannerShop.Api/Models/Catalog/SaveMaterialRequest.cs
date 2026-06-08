using System.ComponentModel.DataAnnotations;

namespace BannerShop.Api.Models.Catalog;

public class SaveMaterialRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 10000)]
    public int WidthCm { get; set; }

    /// <summary>
    /// Maximum banner width (cm) without gluing panels (BANNERSH-88).
    /// 0 = default to <see cref="WidthCm"/>.
    /// </summary>
    [Range(0, 10000)]
    public int MaxBannerWidthCm { get; set; }

    [Range(1, 10000)]
    public int WeightGsm { get; set; }

    [Range(0, 1000000)]
    public decimal PricePerSqm { get; set; }

    public DateTime? AvailableFrom { get; set; }
}
