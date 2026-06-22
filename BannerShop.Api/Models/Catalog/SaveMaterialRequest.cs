using System.ComponentModel.DataAnnotations;

namespace BannerShop.Api.Models.Catalog;

public class SaveMaterialRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 10000)]
    public int WidthCm { get; set; }

    [Range(1, 10000)]
    public int WeightGsm { get; set; }

    [Range(0, 1000000)]
    public decimal PricePerSqm { get; set; }

    public DateTime? AvailableFrom { get; set; }
}
