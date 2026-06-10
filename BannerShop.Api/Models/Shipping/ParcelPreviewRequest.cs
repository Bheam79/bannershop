using System.ComponentModel.DataAnnotations;
using BannerShop.Core.Enums;

namespace BannerShop.Api.Models.Shipping;

/// <summary>
/// Request body for <c>POST /api/shipping/parcel-preview</c> — a lightweight
/// "what will Bring receive" calculation that returns only the parcel dimensions
/// and weight, with no postal code or carrier call. Used by the checkout UI to
/// show calculated dims + weight under each Pakkemetode option. BANNERSH-180.
/// </summary>
public class ParcelPreviewRequest
{
    [Range(1, int.MaxValue)]
    public int BannerSizeId { get; set; }

    /// <summary>Required when the chosen banner size has IsCustomWidth = true.</summary>
    [Range(50, 1000)]
    public int? CustomWidthCm { get; set; }

    [Range(1, 1000)]
    public int Qty { get; set; } = 1;

    /// <summary>How the customer wants the order packaged.</summary>
    public PackingMode PackingMode { get; set; } = PackingMode.Folded;
}
