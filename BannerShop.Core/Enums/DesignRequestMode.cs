namespace BannerShop.Core.Enums;

/// <summary>
/// What kind of design request the customer placed.
/// <see cref="Ai"/> (95 kr) runs through the AI image pipeline,
/// <see cref="Manual"/> (495 kr) is a human-designer order.
///
/// Stored as a string in the DB.
/// </summary>
public enum DesignRequestMode
{
    Ai = 1,
    Manual = 2
}
