namespace BannerShop.Api.Services.BannerBuilder;

/// <summary>
/// Configuration for where all banner-builder uploads live and how large they can be.
/// Bound from the "FileStorage" section in appsettings.json.
///
/// New canonical property names (BANNERSH-25):
///   LocalRoot       – filesystem root for all uploaded files
///   PublicBaseUrl   – URL prefix served by StaticFiles middleware at "/files"
///   MaxUploadBytes  – hard limit enforced by Kestrel + UploadValidator
///   AllowedMimeTypes – MIME types accepted by UploadValidator
///   Provider        – "LocalDisk" (future: "AzureBlob", "S3")
///
/// The legacy aliases BasePath / PublicUrlPrefix / MaxFileSizeMb remain for
/// backward compatibility with code written before this refactor and will be
/// removed once all callers migrate to the new names.
/// </summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    // ── New canonical properties ──────────────────────────────────────────────

    /// <summary>
    /// Storage provider selector. Only "LocalDisk" is implemented; reserved for future cloud backends.
    /// </summary>
    public string Provider { get; set; } = "LocalDisk";

    /// <summary>Absolute filesystem path under which all uploads are stored.</summary>
    public string LocalRoot { get; set; } = "/workspace/uploads";

    /// <summary>
    /// URL request path prefix where StaticFiles serves uploaded files (default "/files").
    /// Only files stored with unguessable GUIDs should be directly accessible this way;
    /// design-request previews that need authorization should go through a controller.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "/files";

    /// <summary>Maximum accepted upload size in bytes (default 50 MB).</summary>
    public long MaxUploadBytes { get; set; } = 50L * 1024 * 1024;

    /// <summary>
    /// MIME types accepted by <see cref="UploadValidator"/>.
    /// The validator also performs magic-byte verification so Content-Type alone is not trusted.
    /// </summary>
    public string[] AllowedMimeTypes { get; set; } =
    [
        "image/jpeg",
        "image/png",
        "image/webp",
        "application/pdf",
    ];

    // ── Legacy aliases (kept for backward compat with code pre-BANNERSH-25) ──

    /// <summary>Legacy alias for <see cref="LocalRoot"/>. Prefer LocalRoot in new code.</summary>
    [System.Obsolete("Use LocalRoot")]
    public string BasePath
    {
        get => LocalRoot;
        set => LocalRoot = value;
    }

    /// <summary>Legacy alias for <see cref="PublicBaseUrl"/>. Prefer PublicBaseUrl in new code.</summary>
    [System.Obsolete("Use PublicBaseUrl")]
    public string PublicUrlPrefix
    {
        get => PublicBaseUrl;
        set => PublicBaseUrl = value;
    }

    /// <summary>Legacy alias: max upload in whole megabytes. Use <see cref="MaxUploadBytes"/> for precision.</summary>
    [System.Obsolete("Use MaxUploadBytes")]
    public int MaxFileSizeMb
    {
        get => (int)(MaxUploadBytes / (1024 * 1024));
        set => MaxUploadBytes = (long)value * 1024 * 1024;
    }
}
