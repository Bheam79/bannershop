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
    ManualDesign = 2,

    /// <summary>
    /// AI generation credit pack purchase (BANNERSH-139). These orders track revenue
    /// for transaction reports but are hidden by default in the admin orders list so
    /// the production team isn't distracted by them when looking at what to print.
    /// Items collection holds a single synthetic line describing the pack.
    /// </summary>
    CreditPack = 3
}
