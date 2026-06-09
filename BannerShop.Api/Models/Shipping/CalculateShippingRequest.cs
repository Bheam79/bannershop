using System.ComponentModel.DataAnnotations;
using BannerShop.Core.Enums;

namespace BannerShop.Api.Models.Shipping;

public class CalculateShippingRequest
{
    [Required, StringLength(20, MinimumLength = 4)]
    public string PostalCode { get; set; } = string.Empty;

    [StringLength(100)]
    public string? City { get; set; }

    [Range(1, int.MaxValue)]
    public int BannerSizeId { get; set; }

    /// <summary>Required when the chosen banner size has IsCustomWidth = true.</summary>
    [Range(50, 1000)]
    public int? CustomWidthCm { get; set; }

    [Range(1, 1000)]
    public int Qty { get; set; } = 1;

    /// <summary>
    /// How the customer wants the order packaged. Defaults to <see cref="PackingMode.Rolled"/>
    /// (matches the historical behaviour). BANNERSH-143.
    /// </summary>
    public PackingMode PackingMode { get; set; } = PackingMode.Rolled;
}
