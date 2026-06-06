namespace BannerShop.Api.Services.BannerBuilder;

/// <summary>
/// Configuration for where banner-builder uploads live and how large they can be.
/// Bound from the "FileStorage" section in appsettings.json.
///
/// A more general IFileStore abstraction is planned in BANNERSH-25; for now this
/// is the single concrete location all banner-builder file ops point at.
/// </summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Absolute filesystem path under which all uploads are stored.</summary>
    public string BasePath { get; set; } = "/workspace/uploads";

    /// <summary>Maximum accepted upload size in megabytes.</summary>
    public int MaxFileSizeMb { get; set; } = 50;

    /// <summary>URL path prefix where uploaded files are served from (via StaticFiles).</summary>
    public string PublicUrlPrefix { get; set; } = "/uploads";
}
