using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BannerShop.Core.Enums;
using BannerShop.Core.Helpers;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BannerShop.Api.Services.BannerBuilder;

/// <summary>
/// Generates server-side banner preview images with eyelet overlays and serves them
/// via opaque GUID-based URLs so callers cannot manipulate the output dimensions.
///
/// Preview size is fixed at <see cref="FixedMaxPx"/> pixels on the longer side —
/// this value is intentionally not a public API parameter.
///
/// Cache: previews are written to {LocalRoot}/banner-builder/preview-cache/{guid}.jpg.
/// The GUID is a deterministic MD5 hash of (sourceStoragePath, widthCm, heightCm, eyelet)
/// so the same inputs always reuse the same cache file without a database lookup.
/// </summary>
public sealed class BannerPreviewService
{
    // ── Fixed server-controlled constants (never exposed to callers) ───────────
    /// <summary>Maximum pixel length on the longer side of the generated preview.</summary>
    private const int FixedMaxPx = 800;

    /// <summary>
    /// Eyelet circle radius in pixels.  Fixed at 4 (⇒ 8 px diameter) so eyelets remain
    /// clearly visible even on small thumbnails.
    /// </summary>
    private const int EyeletRadius = 4;

    // Bright red fill + 1 px black border for maximum visibility.
    private static readonly Rgba32 EyeletFill   = new(255, 40, 40, 255);
    private static readonly Rgba32 EyeletBorder = new(  0,  0,  0, 255);

    // Validates that a GUID string is exactly 32 lowercase hex digits (our MD5 format).
    private static readonly Regex GuidPattern =
        new(@"^[0-9a-f]{32}$", RegexOptions.Compiled);

    private readonly BannerFileStorage _storage;
    private readonly ILogger<BannerPreviewService> _logger;

    public BannerPreviewService(
        BannerFileStorage storage,
        ILogger<BannerPreviewService> logger)
    {
        _storage = storage;
        _logger  = logger;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the opaque GUID for a banner preview with eyelet overlay.
    /// If the preview has not been generated yet it is created and cached.
    ///
    /// Pass the result to <see cref="ResolvePreviewPath"/> to get the filesystem path,
    /// or surface it to the frontend as <c>/api/banner-preview/{guid}</c>.
    /// </summary>
    /// <param name="sourceStoragePath">
    ///   Relative storage path of the source image (e.g. <c>BannerDesign.PreviewStoragePath</c>
    ///   or <c>BannerDesign.StoragePath</c>).
    /// </param>
    /// <param name="widthCm">Banner physical width in cm (used for eyelet placement).</param>
    /// <param name="heightCm">Banner physical height in cm (used for eyelet placement).</param>
    /// <param name="eyelet">Which eyelet option to overlay.</param>
    public async Task<string> GetPreviewGuidAsync(
        string sourceStoragePath,
        int widthCm,
        int heightCm,
        EyeletOption eyelet,
        CancellationToken ct = default)
    {
        var guid = ComputeGuid(sourceStoragePath, widthCm, heightCm, eyelet);
        var cachePath = GetCachePath(guid);

        if (!File.Exists(cachePath))
        {
            var sourceAbs = _storage.AbsolutePathFor(sourceStoragePath);
            await GenerateCachedPreviewAsync(sourceAbs, cachePath, widthCm, heightCm, eyelet, ct);
        }

        return guid;
    }

    /// <summary>
    /// Returns the absolute filesystem path for a cached preview,
    /// or <see langword="null"/> if the GUID is invalid or the cache file is missing.
    /// </summary>
    public string? ResolvePreviewPath(string? guid)
    {
        if (guid is null || !GuidPattern.IsMatch(guid)) return null;
        var path = GetCachePath(guid);
        return File.Exists(path) ? path : null;
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    private async Task GenerateCachedPreviewAsync(
        string sourceAbsPath,
        string outputAbsPath,
        int widthCm,
        int heightCm,
        EyeletOption eyelet,
        CancellationToken ct)
    {
        if (!File.Exists(sourceAbsPath))
        {
            _logger.LogWarning(
                "BannerPreviewService: source file not found at {Path}; skipping generation.",
                sourceAbsPath);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputAbsPath)!);

        using var img = await Image.LoadAsync<Rgba32>(sourceAbsPath, ct);

        // ── Resize so the longer side ≤ FixedMaxPx ──────────────────────────────
        var (origW, origH) = (img.Width, img.Height);
        var longer = Math.Max(origW, origH);
        if (longer > FixedMaxPx)
        {
            var scale = (double)FixedMaxPx / longer;
            img.Mutate(ctx => ctx.Resize(
                (int)Math.Round(origW * scale),
                (int)Math.Round(origH * scale)));
        }

        // ── Draw eyelet circles ──────────────────────────────────────────────────
        if (eyelet != EyeletOption.None && widthCm > 0 && heightCm > 0)
        {
            var positions = EyeletPositionHelper.GetPixelPositions(
                img.Width, img.Height, widthCm, heightCm, eyelet);

            if (positions.Count > 0)
                DrawEyelets(img, positions);
        }

        await img.SaveAsync(outputAbsPath, new JpegEncoder { Quality = 82 }, ct);

        _logger.LogDebug(
            "BannerPreviewService: generated preview {Output} ({W}×{H}px, eyelet={Eyelet}).",
            outputAbsPath, img.Width, img.Height, eyelet);
    }

    /// <summary>
    /// Draws all eyelet circles on the image in a single <c>ProcessPixelRows</c> pass.
    /// Each eyelet is an 8 px diameter bright-red filled circle with a 1 px black border.
    /// </summary>
    private static void DrawEyelets(Image<Rgba32> img, IReadOnlyList<(int X, int Y)> positions)
    {
        const int r = EyeletRadius; // compile-time constant inside the lambda

        img.ProcessPixelRows(accessor =>
        {
            foreach (var (cx, cy) in positions)
            {
                for (int dy = -(r + 1); dy <= r + 1; dy++)
                {
                    int py = cy + dy;
                    if (py < 0 || py >= accessor.Height) continue;

                    var row = accessor.GetRowSpan(py);
                    for (int dx = -(r + 1); dx <= r + 1; dx++)
                    {
                        int px = cx + dx;
                        if (px < 0 || px >= row.Length) continue;

                        double dist = Math.Sqrt(dx * dx + dy * dy);
                        if (dist <= r)
                            row[px] = EyeletFill;
                        else if (dist <= r + 1.0)
                            row[px] = EyeletBorder;
                    }
                }
            }
        });
    }

    private string GetCachePath(string guid)
    {
        // Stored under {LocalRoot}/banner-builder/preview-cache/{guid}.jpg
        // so it lives alongside the existing banner-builder uploads.
        var cacheDir = Path.Combine(
            _storage.AbsolutePathFor(BannerFileStorage.SubFolder),
            "preview-cache");
        return Path.Combine(cacheDir, $"{guid}.jpg");
    }

    /// <summary>
    /// Computes a deterministic, path-safe GUID (lowercase hex MD5) from the inputs.
    /// Same inputs always map to the same GUID → same cache file.
    /// </summary>
    private static string ComputeGuid(
        string sourceStoragePath, int widthCm, int heightCm, EyeletOption eyelet)
    {
        var key = $"{sourceStoragePath}|{widthCm}|{heightCm}|{(int)eyelet}|{FixedMaxPx}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
