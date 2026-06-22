using System.ComponentModel.DataAnnotations;

namespace BannerShop.Api.Models.Catalog;

/// <summary>
/// Body for <c>POST/PUT /api/admin/sizes</c>. BANNERSH-255 — range-based pricing rules.
/// </summary>
public class SaveBannerSizeRequest : IValidatableObject
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    [Required]
    public int MaterialId { get; set; }

    public int SortOrder { get; set; }

    [Range(1, 10000)]
    public int MinWidthCm { get; set; }

    [Range(1, 10000)]
    public int MaxWidthCm { get; set; }

    [Range(1, 10000)]
    public int MinHeightCm { get; set; }

    [Range(1, 10000)]
    public int MaxHeightCm { get; set; }

    [Range(1, 10000)]
    public int PricingHeightCm { get; set; }

    [Range(1, 20)]
    public int PricingMultiplier { get; set; } = 1;

    [Range(0, 1000000)]
    public decimal? FixedPrice { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext ctx)
    {
        if (MaxWidthCm < MinWidthCm)
            yield return new ValidationResult("MaxWidthCm must be ≥ MinWidthCm.", new[] { nameof(MaxWidthCm) });
        if (MaxHeightCm < MinHeightCm)
            yield return new ValidationResult("MaxHeightCm must be ≥ MinHeightCm.", new[] { nameof(MaxHeightCm) });
    }
}
