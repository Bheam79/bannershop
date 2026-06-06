namespace BannerShop.Api.Models.Templates;

/// <summary>DTO for GET /api/templates list items.</summary>
public sealed class BannerTemplateDto
{
    public int Id { get; set; }

    /// <summary>String form of the <see cref="BannerShop.Core.Enums.BannerTemplateCategory"/> enum (e.g. "Birthday").</summary>
    public string Category { get; set; } = string.Empty;

    public string NameNb { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
