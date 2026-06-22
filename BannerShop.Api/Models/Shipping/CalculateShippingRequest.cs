using System.ComponentModel.DataAnnotations;
using BannerShop.Core.Enums;

namespace BannerShop.Api.Models.Shipping;

/// <summary>
/// Body for <c>POST /api/shipping/calculate</c>. BANNERSH-255 — banner dimensions are
/// passed directly (the catalogue no longer exposes a single concrete size per row).
/// </summary>
public class CalculateShippingRequest
{
    [Required, StringLength(20, MinimumLength = 4)]
    public string PostalCode { get; set; } = string.Empty;

    [StringLength(100)]
    public string? City { get; set; }

    [Required, Range(1, int.MaxValue)]
    public int MaterialId { get; set; }

    [Range(1, 1000)]
    public int WidthCm { get; set; }

    [Range(1, 1000)]
    public int HeightCm { get; set; }

    [Range(1, 1000)]
    public int Qty { get; set; } = 1;

    /// <summary>
    /// How the customer wants the order packaged. Defaults to <see cref="PackingMode.Folded"/>
    /// per BANNERSH-174.
    /// </summary>
    public PackingMode PackingMode { get; set; } = PackingMode.Folded;
}
