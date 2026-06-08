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
    Other = 7,
    /// <summary>
    /// Norwegian "dåp" — Christian baptism / christening. Added in BANNERSH-61
    /// alongside the other person-centred celebrations (Birthday, Confirmation,
    /// Wedding) that support optional portrait upload + gpt-image-2 edit merge.
    /// </summary>
    Baptism = 8
}

/// <summary>
/// Extension helpers for <see cref="BannerTemplateCategory"/> — pure functions
/// that don't need DB access. Kept in the same file to avoid splintering the
/// enum across the project.
/// </summary>
public static class BannerTemplateCategoryExtensions
{
    /// <summary>
    /// True for celebrations that revolve around a single person (or couple)
    /// and therefore benefit from an uploaded portrait + gpt-image-2 edit merge.
    /// See BANNERSH-61.
    /// </summary>
    public static bool IsPersonCentred(this BannerTemplateCategory category) => category switch
    {
        BannerTemplateCategory.Birthday     => true,
        BannerTemplateCategory.Confirmation => true,
        BannerTemplateCategory.Baptism      => true,
        BannerTemplateCategory.Wedding      => true,
        _                                   => false
    };
}
