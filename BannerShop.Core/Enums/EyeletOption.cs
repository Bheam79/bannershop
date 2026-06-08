namespace BannerShop.Core.Enums;

/// <summary>
/// Eyelet (malje) finishing option for a banner.
/// Hem/sewing is not possible on PVC banners — only eyelet options are offered.
/// </summary>
public enum EyeletOption
{
    /// <summary>No eyelets.</summary>
    None = 0,

    /// <summary>One eyelet at each corner (4 total).</summary>
    FourCorners = 1,

    /// <summary>
    /// One eyelet per ~100 cm along each side, starting at the corners and
    /// working inwards using the iterative spacing formula.
    /// </summary>
    PerMeter = 2
}
