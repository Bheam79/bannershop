namespace BannerShop.Core.Enums;

/// <summary>
/// Classifies the fulfilment flow for an order.
/// Stored as a tinyint in the DB.
/// </summary>
public enum OrderType : byte
{
    /// <summary>Customer uploads their own print-ready artwork.</summary>
    CustomBanner = 0,

    /// <summary>Design was produced by the AI generation pipeline.</summary>
    AiBanner = 1,

    /// <summary>Design is delegated to a human designer (495 kr design fee).</summary>
    ManualDesign = 2
}
