namespace BannerShop.Core.Enums;

/// <summary>
/// How a banner order is packed for shipping. Customer-selectable on the cart
/// (BANNERSH-143). Affects the parcel dimensions calculated by
/// <c>ParcelCalculator</c> and therefore the Bring shipping quote.
/// </summary>
public enum PackingMode
{
    /// <summary>
    /// Default. Banner is rolled into a tube whose length equals the banner's
    /// shortest side + 2 cm. Width/height are 9 cm × 9 cm + 0.5 cm per metre of
    /// the long side.
    /// </summary>
    Rolled,

    /// <summary>
    /// Banner is folded into a flat parcel of 50 × 60 cm with height
    /// 10 cm + 1 cm per metre of the long side.
    /// </summary>
    Folded,
}
