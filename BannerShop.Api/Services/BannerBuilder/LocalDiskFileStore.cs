using System.Diagnostics.CodeAnalysis;
using BannerShop.Core;
using Microsoft.Extensions.Options;

namespace BannerShop.Api.Services.BannerBuilder;

/// <summary>
/// <see cref="IFileStore"/> backed by the local filesystem.
///
/// Layout under <see cref="FileStorageOptions.LocalRoot"/>:
///   banner-builder/{userId}/{guid}.{ext}           – customer-uploaded originals
///   banner-builder/{userId}/{guid}.preview.jpg     – generated preview thumbnails
///   design-requests/{requestId}/{guid}.{ext}       – AI / manual design-request assets
///
/// Path safety: <see cref="NormalizePath"/> rejects segments that contain ".." or
/// that resolve outside the configured root, preventing directory-traversal attacks.
///
/// StaticFiles middleware (configured in Program.cs) exposes this root at the
/// <see cref="FileStorageOptions.PublicBaseUrl"/> prefix ("/files" by default).
/// Files stored with unguessable GUIDs are safe to serve publicly.
/// Design-request previews that require authorization should be proxied through
/// an API controller rather than served directly by StaticFiles.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Local filesystem I/O — tested via integration with real files")]
public sealed class LocalDiskFileStore : IFileStore
{
    private readonly FileStorageOptions _options;
    private readonly ILogger<LocalDiskFileStore> _logger;

    public LocalDiskFileStore(IOptions<FileStorageOptions> options, ILogger<LocalDiskFileStore> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    // ── IFileStore ────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<StoredFile> SaveAsync(
        Stream content,
        string contentType,
        string subPath,
        string fileName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Build and validate the full relative path.
        var relPath = NormalizePath(subPath, fileName);
        var absPath = ToAbsolute(relPath);

        // Ensure the directory tree exists.
        var dir = Path.GetDirectoryName(absPath)!;
        Directory.CreateDirectory(dir);

        long size;
        await using (var fs = File.Create(absPath))
        {
            await content.CopyToAsync(fs, ct);
            size = fs.Length;
        }

        _logger.LogDebug("Stored file at {RelPath} ({Bytes} bytes)", relPath, size);

        return new StoredFile(
            StoragePath: relPath,
            PublicUrl: GetPublicUrl(relPath),
            SizeBytes: size);
    }

    /// <inheritdoc/>
    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default)
    {
        var abs = ToAbsolute(storagePath);
        if (!File.Exists(abs))
            throw new FileNotFoundException($"Stored file not found: {storagePath}", abs);

        Stream stream = File.OpenRead(abs);
        return Task.FromResult(stream);
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        var abs = ToAbsolute(storagePath);
        if (!File.Exists(abs))
            return Task.FromResult(false);

        try
        {
            File.Delete(abs);
            _logger.LogDebug("Deleted stored file {StoragePath}", storagePath);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete stored file {StoragePath}", storagePath);
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    public string GetPublicUrl(string storagePath)
        => $"{_options.PublicBaseUrl.TrimEnd('/')}/{storagePath.TrimStart('/')}";

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns the absolute path for <paramref name="storagePath"/> under <see cref="FileStorageOptions.LocalRoot"/>.</summary>
    public string ToAbsolute(string storagePath)
    {
        // storagePath may already be a relative path (no root) or come in with forward slashes.
        var normalized = storagePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        return Path.Combine(_options.LocalRoot, normalized);
    }

    /// <summary>
    /// Joins <paramref name="subPath"/> and <paramref name="fileName"/> into a
    /// slash-separated relative storage path and rejects traversal attempts.
    /// </summary>
    /// <exception cref="ArgumentException">If any component is rooted or contains "..".</exception>
    public static string NormalizePath(string subPath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(subPath))
            throw new ArgumentException("subPath must not be empty.", nameof(subPath));
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("fileName must not be empty.", nameof(fileName));

        // Reject absolute paths or traversal in any component.
        static void AssertSafe(string part, string paramName)
        {
            if (Path.IsPathRooted(part))
                throw new ArgumentException($"{paramName} must not be a rooted path.", paramName);

            var segments = part.Replace('\\', '/').Split('/');
            foreach (var seg in segments)
            {
                if (seg == "..")
                    throw new ArgumentException($"{paramName} must not contain '..' segments.", paramName);
            }
        }

        AssertSafe(subPath, nameof(subPath));
        AssertSafe(fileName, nameof(fileName));

        return $"{subPath.Trim('/').Replace('\\', '/')}/{fileName.Replace('\\', '/')}";
    }

    /// <summary>Generates a unique leaf file name for a new upload.</summary>
    public static string NewFileName(string extension)
    {
        var ext = (extension ?? string.Empty).TrimStart('.').ToLowerInvariant();
        return $"{Guid.NewGuid():N}.{ext}";
    }
}
