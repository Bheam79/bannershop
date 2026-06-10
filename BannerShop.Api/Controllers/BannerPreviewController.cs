using System.Security.Claims;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Controllers;

/// <summary>
/// Unified banner-preview endpoint.
///
/// Two routes:
///
///   GET /api/banner-preview/generate?designId={id}&amp;eyelet={option}
///     Resolves the BannerDesign, generates (or returns a cached) preview JPEG
///     with eyelet circles overlaid, and returns the opaque GUID URL.
///     Preview size is server-controlled — callers cannot influence it via URL parameters.
///
///   GET /api/banner-preview/{guid}
///     Serves a cached preview JPEG by its opaque GUID.
///     Only GUIDs produced by the "generate" route above are valid; the path is
///     validated server-side to prevent path traversal.
/// </summary>
[ApiController]
[Route("api/banner-preview")]
[AllowAnonymous] // same policy as BannerBuilderController — preview JPEGs are not sensitive
public class BannerPreviewController : ControllerBase
{
    private readonly BannerPreviewService _previews;
    private readonly BannerShopDbContext  _db;
    private readonly ILogger<BannerPreviewController> _logger;

    public BannerPreviewController(
        BannerPreviewService previews,
        BannerShopDbContext db,
        ILogger<BannerPreviewController> logger)
    {
        _previews = previews;
        _db       = db;
        _logger   = logger;
    }

    // ── GET /api/banner-preview/generate ─────────────────────────────────────

    /// <summary>
    /// Generates (or returns a cached) banner preview for the given BannerDesign
    /// with the specified eyelet option overlaid.
    ///
    /// Returns <c>{ previewUrl, guid }</c>.  The <c>previewUrl</c> is an opaque
    /// GUID-keyed path — callers cannot adjust the image dimensions by changing
    /// URL parameters.
    /// </summary>
    [HttpGet("generate")]
    public async Task<IActionResult> Generate(
        [FromQuery] int designId,
        [FromQuery] EyeletOption eyelet = EyeletOption.None,
        CancellationToken ct = default)
    {
        var userId = GetUserId();

        var design = await _db.BannerDesigns
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == designId, ct);

        if (design is null) return NotFound(new { error = "BannerDesign not found." });

        // Use the stored preview thumbnail as source when available (it is already
        // rotated and downscaled — faster and avoids a second full decode of the
        // original).  Fall back to the full-resolution original when no preview exists.
        var sourcePath = string.IsNullOrWhiteSpace(design.PreviewStoragePath)
            ? design.StoragePath
            : design.PreviewStoragePath;

        if (string.IsNullOrWhiteSpace(sourcePath))
            return NotFound(new { error = "No source image available for this design." });

        // widthCm and heightCm drive eyelet-position maths.
        // ComputedWidthCm is 0 for newly uploaded anonymous designs before height is
        // chosen — fall back to a safe non-zero value so positions are still computed.
        int widthCm  = design.ComputedWidthCm  > 0 ? design.ComputedWidthCm  : 100;
        int heightCm = design.SelectedHeightCm > 0 ? design.SelectedHeightCm : 150;

        var guid = await _previews.GetPreviewGuidAsync(sourcePath, widthCm, heightCm, eyelet, ct);
        var previewUrl = Url.Action(nameof(Serve), new { guid })
                         ?? $"/api/banner-preview/{guid}";

        return Ok(new { previewUrl, guid });
    }

    // ── GET /api/banner-preview/{guid} ─────────────────────────────────────────

    /// <summary>
    /// Serves a cached banner preview JPEG by its opaque GUID.
    /// The GUID is validated server-side — only exact 32-char lowercase hex strings
    /// produced by the generate endpoint are accepted.
    /// </summary>
    [HttpGet("{guid}")]
    public IActionResult Serve(string guid)
    {
        var path = _previews.ResolvePreviewPath(guid);
        if (path is null) return NotFound();

        // Stream directly; no ETag / caching headers needed for MVP.
        var stream = System.IO.File.OpenRead(path);
        return File(stream, "image/jpeg");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private int? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }
}
