using System.ComponentModel.DataAnnotations;

namespace BannerShop.Api.Models.Catalog;

public class SaveBannerSizeRequest
{
    public int? WidthCm { get; set; }

    [Range(1, 10000)]
    public int HeightCm { get; set; }

    public bool IsCustomWidth { get; set; }

    public bool IsCustomHeight { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    [Required]
    public int MaterialId { get; set; }

    [Range(0, 1000000)]
    public decimal? FixedPrice { get; set; }

    public int SortOrder { get; set; }
}
