using BannerShop.Core.Enums;

namespace BannerShop.Core.Entities;

/// <summary>
/// A pre-defined banner template (celebration category) shown to customers as a
/// starting point for the banner builder. Templates are seeded and publicly listed
/// via GET /api/templates.
/// </summary>
public class BannerTemplate
{
    public int Id { get; set; }

    /// <summary>Celebration category — stable enum stored as string in DB.</summary>
    public BannerTemplateCategory Category { get; set; }

    /// <summary>Norwegian (bokmål) display name.</summary>
    public string NameNb { get; set; } = string.Empty;

    /// <summary>English display name.</summary>
    public string NameEn { get; set; } = string.Empty;

    /// <summary>Sort order for stable, designer-controlled UI ordering.</summary>
    public int SortOrder { get; set; }
}
