using Microsoft.Extensions.Options;

namespace BannerShop.Api.Services.BannerBuilder;

/// <summary>
/// Thin wrapper around the local filesystem for banner-builder uploads.
/// Centralises path computation so the controller stays free of path-fiddling.
///
/// Layout: {BasePath}/banner-builder/{userId}/{guid}.{ext}
///         {BasePath}/banner-builder/{userId}/{guid}.preview.jpg
///
/// This class predates the IFileStore abstraction (BANNERSH-25) and will be migrated
/// to use IFileStore directly in a future task. In the meantime it reads the same
/// FileStorageOptions and its methods delegate to the legacy alias properties.
/// </summary>
#pragma warning disable CS0618 // using legacy FileStorageOptions aliases intentionally
public sealed class BannerFileStorage
{
    private readonly FileStorageOptions _options;
    public const string SubFolder = "banner-builder";

    public BannerFileStorage(IOptions<FileStorageOptions> options)
    {
        _options = options.Value;
    }

    public long MaxFileSizeBytes => _options.MaxUploadBytes;

    /// <summary>Returns the absolute folder path for one user's banner-builder files (creating it if needed).</summary>
    public string EnsureUserDirectory(int userId)
    {
        var dir = Path.Combine(_options.LocalRoot, SubFolder, userId.ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Returns a unique filename (just the leaf name) for a fresh upload.</summary>
    public static string NewFileName(string extension)
    {
        var ext = (extension ?? string.Empty).TrimStart('.').ToLowerInvariant();
        return $"{Guid.NewGuid():N}.{ext}";
    }

    /// <summary>Returns the relative storage path (under BasePath) given the user and leaf filename.</summary>
    public static string RelativePathFor(int userId, string fileName)
        => $"{SubFolder}/{userId}/{fileName}";

    /// <summary>Returns the absolute filesystem path for a given storage path.</summary>
    public string AbsolutePathFor(string relativeStoragePath)
        => Path.Combine(_options.LocalRoot, relativeStoragePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Returns the public URL where a stored file is served.</summary>
    public string PublicUrlFor(string relativeStoragePath)
        => $"{_options.PublicBaseUrl.TrimEnd('/')}/{relativeStoragePath}";

    /// <summary>Deletes a stored file if present. Silently succeeds when missing.</summary>
    public void TryDelete(string? relativeStoragePath)
    {
        if (string.IsNullOrWhiteSpace(relativeStoragePath)) return;
        var abs = AbsolutePathFor(relativeStoragePath);
        try { if (File.Exists(abs)) File.Delete(abs); } catch { /* best effort */ }
    }
}
#pragma warning restore CS0618
