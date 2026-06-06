using Microsoft.Extensions.Options;

namespace BannerShop.Api.Services.BannerBuilder;

/// <summary>
/// Centralised upload-validation helper used by all banner-builder flows.
///
/// Checks performed (in order):
/// 1. File presence and non-zero size.
/// 2. Maximum file size (from <see cref="FileStorageOptions.MaxUploadBytes"/>).
/// 3. Content-Type is in the <see cref="FileStorageOptions.AllowedMimeTypes"/> allow-list.
/// 4. Magic-byte sniff — the first 16 bytes must match the declared type's known signatures.
///    This prevents trivially disguised uploads (e.g. a PHP script with Content-Type image/jpeg).
/// </summary>
public sealed class UploadValidator
{
    // Known file signatures (magic bytes).
    // JPEG: starts with FF D8 FF
    private static readonly (byte[] Sig, int Offset)[] JpegSigs =
    [
        ([0xFF, 0xD8, 0xFF], 0),
    ];
    // PNG: starts with 89 50 4E 47 0D 0A 1A 0A
    private static readonly (byte[] Sig, int Offset)[] PngSigs =
    [
        ([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], 0),
    ];
    // PDF: starts with "%PDF" (25 50 44 46)
    private static readonly (byte[] Sig, int Offset)[] PdfSigs =
    [
        ([0x25, 0x50, 0x44, 0x46], 0),
    ];
    // WEBP: "RIFF" at offset 0, then "WEBP" at offset 8
    private static readonly (byte[] Sig, int Offset)[] WebpSigs =
    [
        ([0x52, 0x49, 0x46, 0x46], 0), // "RIFF"
        ([0x57, 0x45, 0x42, 0x50], 8), // "WEBP"
    ];

    private static readonly IReadOnlyDictionary<string, (byte[] Sig, int Offset)[]> MagicByMime =
        new Dictionary<string, (byte[], int)[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = JpegSigs,
            ["image/jpg"]  = JpegSigs,
            ["image/png"]  = PngSigs,
            ["image/webp"] = WebpSigs,
            ["application/pdf"] = PdfSigs,
        };

    /// <summary>File extension inferred from MIME type.</summary>
    public static readonly IReadOnlyDictionary<string, string> ExtByMime =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = "jpg",
            ["image/jpg"]  = "jpg",
            ["image/png"]  = "png",
            ["image/webp"] = "webp",
            ["application/pdf"] = "pdf",
        };

    private readonly FileStorageOptions _options;

    public UploadValidator(IOptions<FileStorageOptions> options)
    {
        _options = options.Value;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates an <see cref="IFormFile"/> upload.
    /// Returns <c>(true, null, ext)</c> on success, <c>(false, errorMessage, null)</c> on failure.
    /// </summary>
    public async Task<(bool Valid, string? Error, string? Extension)> ValidateAsync(
        IFormFile? file,
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return (false, "No file uploaded.", null);

        if (file.Length > _options.MaxUploadBytes)
        {
            var maxMb = _options.MaxUploadBytes / (1024 * 1024);
            return (false, $"File too large (max {maxMb} MB).", null);
        }

        var mime = (file.ContentType ?? string.Empty).Trim().ToLowerInvariant();

        if (!_options.AllowedMimeTypes.Contains(mime, StringComparer.OrdinalIgnoreCase))
            return (false, "Unsupported file type. Allowed: JPEG, PNG, WEBP, PDF.", null);

        if (!ExtByMime.TryGetValue(mime, out var ext))
            return (false, "Unsupported file type. Allowed: JPEG, PNG, WEBP, PDF.", null);

        if (!await VerifyMagicBytesAsync(file, mime, ct))
            return (false, "File contents do not match the declared type.", null);

        return (true, null, ext);
    }

    /// <summary>
    /// Validates a raw stream + MIME type (e.g. when the file was read from a URL rather than a form upload).
    /// The stream position is reset to 0 after the check.
    /// </summary>
    public async Task<(bool Valid, string? Error, string? Extension)> ValidateStreamAsync(
        Stream stream,
        string contentType,
        long length,
        CancellationToken ct = default)
    {
        if (stream is null || length == 0)
            return (false, "Empty content.", null);

        if (length > _options.MaxUploadBytes)
        {
            var maxMb = _options.MaxUploadBytes / (1024 * 1024);
            return (false, $"File too large (max {maxMb} MB).", null);
        }

        var mime = (contentType ?? string.Empty).Trim().ToLowerInvariant();

        if (!_options.AllowedMimeTypes.Contains(mime, StringComparer.OrdinalIgnoreCase))
            return (false, "Unsupported file type. Allowed: JPEG, PNG, WEBP, PDF.", null);

        if (!ExtByMime.TryGetValue(mime, out var ext))
            return (false, "Unsupported file type. Allowed: JPEG, PNG, WEBP, PDF.", null);

        if (!await VerifyMagicBytesStreamAsync(stream, mime, ct))
            return (false, "File contents do not match the declared type.", null);

        // Rewind so the caller can read the full stream.
        if (stream.CanSeek)
            stream.Position = 0;

        return (true, null, ext);
    }

    // ── Magic-byte helpers ────────────────────────────────────────────────────

    private static async Task<bool> VerifyMagicBytesAsync(IFormFile file, string mime, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return await VerifyMagicBytesStreamAsync(stream, mime, ct);
    }

    private static async Task<bool> VerifyMagicBytesStreamAsync(Stream stream, string mime, CancellationToken ct)
    {
        if (!MagicByMime.TryGetValue(mime, out var sigs))
            return false;

        // Read enough bytes to cover the deepest signature offset + length.
        int needed = sigs.Max(s => s.Offset + s.Sig.Length);
        var head = new byte[Math.Max(needed, 16)];
        int read = 0;
        int remaining = head.Length;
        while (remaining > 0)
        {
            int chunk = await stream.ReadAsync(head.AsMemory(read, remaining), ct);
            if (chunk == 0) break;
            read += chunk;
            remaining -= chunk;
        }

        if (read < needed)
            return false;

        foreach (var (sig, offset) in sigs)
        {
            if (read < offset + sig.Length) return false;
            for (int i = 0; i < sig.Length; i++)
                if (head[offset + i] != sig[i]) return false;
        }
        return true;
    }
}
