using System.ComponentModel.DataAnnotations;
using BannerShop.Core.Enums;

namespace BannerShop.Api.Models.Shipping;

/// <summary>
/// Request body for <c>POST /api/shipping/parcel-preview</c> — a lightweight
/// "what will Bring receive" calculation that returns only the parcel dimensions
/// and weight, with no postal code or carrier call. Used by the checkout UI to
/// show calculated dims + weight under each Pakkemetode option. BANNERSH-180.
///
/// BANNERSH-255: banner dimensions are passed directly (the catalogue no longer
/// exposes a single concrete size per row).
/// </summary>
public class ParcelPreviewRequest
{
    [Required, Range(1, int.MaxValue)]
    public int MaterialId { get; set; }

    [Range(1, 1000)]
    public int WidthCm { get; set; }

    [Range(1, 1000)]
    public int HeightCm { get; set; }

    [Range(1, 1000)]
    public int Qty { get; set; } = 1;

    /// <summary>How the customer wants the order packaged.</summary>
    public PackingMode PackingMode { get; set; } = PackingMode.Folded;
}
