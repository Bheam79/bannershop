namespace BannerShop.Core.Enums;

/// <summary>
/// Celebration category for the pre-defined banner templates exposed to customers.
/// Numeric values are stable (persisted into the DB) — do not reorder.
/// </summary>
public enum BannerTemplateCategory
{
    Birthday = 1,
    Confirmation = 2,
    Wedding = 3,
    Anniversary = 4,
    Christmas = 5,
    NewYear = 6,
    Other = 7
}
