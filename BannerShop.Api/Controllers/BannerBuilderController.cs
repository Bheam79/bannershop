using System.Security.Claims;
using BannerShop.Api.Models.BannerBuilder;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Controllers;

/// <summary>
/// Basic banner builder: upload a customer-supplied image or PDF, rotate it, choose a height,
/// and persist the resulting BannerDesign. Width is derived from the rotation-effective
/// aspect ratio and the selected height (rounded to nearest 10 cm).
///
/// All endpoints are [AllowAnonymous] since BANNERSH-96: anonymous users can build a banner
/// before registering/logging in. Authentication is required at order-creation time instead.
///
/// See BANNERSH-14 (plan), BANNERSH-15 (original implementation), BANNERSH-96 (anonymous upload).
/// </summary>
[ApiController]
[Route("api/banner-builder")]
[AllowAnonymous]
public class BannerBuilderController : ControllerBase
{
    // Accepted file types (Content-Type + extension hint)
    private static readonly Dictionary<string, string> AcceptedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "jpg",
        ["image/jpg"]  = "jpg",
        ["image/png"]  = "png",
        ["image/webp"] = "webp",
        ["application/pdf"] = "pdf",
    };

    // Magic-byte signatures used to verify the actual file content.
    private static readonly (byte[] Sig, int Offset)[] JpegSigs = { (new byte[] { 0xFF, 0xD8, 0xFF }, 0) };
    private static readonly (byte[] Sig, int Offset)[] PngSigs  = { (new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0) };
    private static readonly (byte[] Sig, int Offset)[] PdfSigs  = { (new byte[] { 0x25, 0x50, 0x44, 0x46 }, 0) }; // "%PDF"
    // WEBP: "RIFF" .... "WEBP" at offset 8
    private static readonly (byte[] Sig, int Offset)[] WebpSigs =
    {
        (new byte[] { 0x52, 0x49, 0x46, 0x46 }, 0),
        (new byte[] { 0x57, 0x45, 0x42, 0x50 }, 8),
    };

    private const int PreviewMaxWidth = 640;
    private const int PreviewQuality = 75;
    private const int DefaultSelectedHeightCm = 150;

    private readonly BannerShopDbContext _db;
    private readonly BannerFileStorage _storage;
    private readonly IImageProcessingService _images;
    private readonly ILogger<BannerBuilderController> _log;

    public BannerBuilderController(
        BannerShopDbContext db,
        BannerFileStorage storage,
        IImageProcessingService images,
        ILogger<BannerBuilderController> log)
    {
        _db = db;
        _storage = storage;
        _images = images;
        _log = log;
    }

    // ── POST /api/banner-builder/upload ───────────────────────────────────────
    [HttpPost("upload")]
    [RequestSizeLimit(75 * 1024 * 1024)] // a little above the 50 MB validation cap
    [RequestFormLimits(MultipartBodyLengthLimit = 75L * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken ct)
    {
        // Authenticated or anonymous — anonymous designs get UserId=null, IP stored.
        var userId = GetUserId();
        var ipAddress = userId is null ? GetClientIpAddress() : null;

        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        if (file.Length > _storage.MaxFileSizeBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { error = $"File too large (max {_storage.MaxFileSizeBytes / (1024 * 1024)} MB)." });

        if (!AcceptedTypes.TryGetValue(file.ContentType ?? string.Empty, out var ext))
            return BadRequest(new { error = "Unsupported file type. Allowed: JPEG, PNG, WEBP, PDF." });

        // Magic-byte verification — never trust Content-Type alone.
        if (!await VerifyMagicBytesAsync(file, ext, ct))
            return BadRequest(new { error = "File contents do not match the declared type." });

        var userDir = _storage.EnsureUserDirectory(userId);

        // Save original (or PDF) to disk
        var originalFileName = BannerFileStorage.NewFileName(ext);
        var originalAbs = Path.Combine(userDir, originalFileName);
        await using (var fs = System.IO.File.Create(originalAbs))
        {
            await file.CopyToAsync(fs, ct);
        }

        // For PDFs, render the first page to PNG and use that as the "image" source.
        string imageRelPath;
        string imageContentType;
        int widthPx, heightPx;

        if (string.Equals(ext, "pdf", StringComparison.OrdinalIgnoreCase))
        {
            var renderedFileName = Path.ChangeExtension(originalFileName, ".png");
            var renderedAbs = Path.Combine(userDir, renderedFileName);
            try
            {
                (widthPx, heightPx) = await _images.RenderPdfFirstPageToPngAsync(originalAbs, renderedAbs, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "PDF rasterization failed for user {UserId}", userId);
                System.IO.File.Delete(originalAbs);
                return BadRequest(new { error = "Could not render PDF. Try uploading an image instead." });
            }
            imageRelPath = BannerFileStorage.RelativePathFor(userId, renderedFileName);
            imageContentType = "image/png";
        }
        else
        {
            (widthPx, heightPx) = await _images.ReadDimensionsAsync(originalAbs, ct);
            imageRelPath = BannerFileStorage.RelativePathFor(userId, originalFileName);
            imageContentType = file.ContentType ?? "application/octet-stream";
        }

        // Preview (no rotation yet; rotation = 0)
        var previewFileName = Path.ChangeExtension(Path.GetFileNameWithoutExtension(originalFileName), ".preview.jpg");
        var previewAbs = Path.Combine(userDir, previewFileName);
        await _images.GeneratePreviewAsync(_storage.AbsolutePathFor(imageRelPath), previewAbs,
            rotationDegrees: 0, maxWidth: PreviewMaxWidth, quality: PreviewQuality, ct);

        var previewRelPath = BannerFileStorage.RelativePathFor(userId, previewFileName);

        var design = new BannerDesign
        {
            UserId = userId,
            IpAddress = ipAddress,
            OriginalFileName = file.FileName,
            StoragePath = imageRelPath,
            ContentType = imageContentType,
            WidthPx = widthPx,
            HeightPx = heightPx,
            RotationDegrees = 0,
            SelectedHeightCm = DefaultSelectedHeightCm,
            ComputedWidthCm = BannerDimensions.ComputeWidthCm(widthPx, heightPx, 0, DefaultSelectedHeightCm),
            PreviewStoragePath = previewRelPath,
            CreatedAt = DateTime.UtcNow
        };
        _db.BannerDesigns.Add(design);
        await _db.SaveChangesAsync(ct);

        return Ok(new UploadResponseDto
        {
            DesignId = design.Id,
            PreviewUrl = Url.Action(nameof(GetPreview), values: new { id = design.Id }) ?? string.Empty,
            WidthPx = design.WidthPx,
            HeightPx = design.HeightPx,
            RotationDegrees = design.RotationDegrees,
            SelectedHeightCm = design.SelectedHeightCm,
            ComputedWidthCm = design.ComputedWidthCm
        });
    }

    // ── PUT /api/banner-builder/{id}/rotate ──────────────────────────────────
    [HttpPut("{id:int}/rotate")]
    public async Task<IActionResult> Rotate(int id, [FromBody] RotateRequestDto req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var userId = GetUserId();

        var design = await _db.BannerDesigns.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (design is null) return NotFound();
        if (!UserCanAccess(design, userId)) return Forbid();

        var newRotation = BannerDimensions.NormalizeRotation(design.RotationDegrees + req.Degrees);
        design.RotationDegrees = newRotation;
        design.ComputedWidthCm = BannerDimensions.ComputeWidthCm(
            design.WidthPx, design.HeightPx, newRotation, design.SelectedHeightCm);

        // Regenerate preview JPEG from the (un-rotated) source so we never lose fidelity.
        if (!string.IsNullOrWhiteSpace(design.PreviewStoragePath))
        {
            var previewAbs = _storage.AbsolutePathFor(design.PreviewStoragePath);
            var sourceAbs = _storage.AbsolutePathFor(design.StoragePath);
            await _images.GeneratePreviewAsync(sourceAbs, previewAbs,
                rotationDegrees: newRotation, maxWidth: PreviewMaxWidth, quality: PreviewQuality, ct);
        }

        await _db.SaveChangesAsync(ct);

        return Ok(new RotateResponseDto
        {
            PreviewUrl = Url.Action(nameof(GetPreview), values: new { id = design.Id }) ?? string.Empty,
            RotationDegrees = design.RotationDegrees,
            ComputedWidthCm = design.ComputedWidthCm,
            ComputedHeightCm = design.SelectedHeightCm
        });
    }

    // ── PUT /api/banner-builder/{id}/height ──────────────────────────────────
    [HttpPut("{id:int}/height")]
    public async Task<IActionResult> SetHeight(int id, [FromBody] HeightRequestDto req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var userId = GetUserId();

        var design = await _db.BannerDesigns.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (design is null) return NotFound();
        if (!UserCanAccess(design, userId)) return Forbid();

        design.SelectedHeightCm = req.HeightCm;
        design.ComputedWidthCm = BannerDimensions.ComputeWidthCm(
            design.WidthPx, design.HeightPx, design.RotationDegrees, design.SelectedHeightCm);

        await _db.SaveChangesAsync(ct);

        return Ok(new HeightResponseDto
        {
            SelectedHeightCm = design.SelectedHeightCm,
            ComputedWidthCm = design.ComputedWidthCm
        });
    }

    // ── GET /api/banner-builder/mine ─────────────────────────────────────────
    /// <summary>
    /// Returns the list of uploaded BannerDesign rows owned by the authenticated user,
    /// excluding designs that are linked as a DesignRequest.FinalBannerDesignId (those
    /// are AI-generated outputs already exposed via the DesignRequests list endpoint).
    /// </summary>
    [HttpGet("mine")]
    [Authorize]
    public async Task<IActionResult> ListMine(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        // Exclude BannerDesigns that are the final output of a design request —
        // those are surfaced via GET /api/design-requests instead.
        var finalDesignIds = (await _db.DesignRequests
            .Where(dr => dr.FinalBannerDesignId != null)
            .Select(dr => dr.FinalBannerDesignId!.Value)
            .ToListAsync(ct))
            .ToHashSet();

        var designs = await _db.BannerDesigns
            .AsNoTracking()
            .Where(d => d.UserId == userId && !finalDesignIds.Contains(d.Id))
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        var items = designs.Select(d => new UploadedDesignListItemDto
        {
            Id = d.Id,
            OriginalFileName = d.OriginalFileName,
            SelectedHeightCm = d.SelectedHeightCm,
            ComputedWidthCm = d.ComputedWidthCm,
            PreviewUrl = d.PreviewStoragePath != null
                ? Url.Action(nameof(GetPreview), values: new { id = d.Id })
                : null,
            CreatedAt = d.CreatedAt,
        }).ToList();

        return Ok(items);
    }

    // ── GET /api/banner-builder/{id} ────────────────────────────────────────
    /// <summary>
    /// Returns the design metadata (dimensions, height) for an existing BannerDesign.
    /// Used by the AI banner wizard after approval so the frontend can compute the price
    /// and pre-populate the cart item before navigating to checkout.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetDesign(int id, CancellationToken ct)
    {
        var userId = GetUserId();

        var design = await _db.BannerDesigns.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (design is null) return NotFound();
        if (!UserCanAccess(design, userId)) return Forbid();

        // Only return a preview URL when the design actually has a preview file —
        // if PreviewStoragePath is null (e.g. designs created via TryCreateFinalBannerDesignAsync
        // before BANNERSH-187 was fixed) the /preview endpoint would return 404, which causes
        // broken <img> tags in the cart and checkout views when the customer re-orders.
        var previewUrl = string.IsNullOrWhiteSpace(design.PreviewStoragePath)
            ? string.Empty
            : Url.Action(nameof(GetPreview), values: new { id = design.Id }) ?? string.Empty;

        return Ok(new UploadResponseDto
        {
            DesignId = design.Id,
            PreviewUrl = previewUrl,
            WidthPx = design.WidthPx,
            HeightPx = design.HeightPx,
            RotationDegrees = design.RotationDegrees,
            SelectedHeightCm = design.SelectedHeightCm,
            ComputedWidthCm = design.ComputedWidthCm
        });
    }

    // ── GET /api/banner-builder/{id}/preview ─────────────────────────────────
    [HttpGet("{id:int}/preview")]
    public async Task<IActionResult> GetPreview(int id, CancellationToken ct)
    {
        var design = await _db.BannerDesigns.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (design is null) return NotFound();
        // Preview images (JPEG thumbnails) are not sensitive; no ownership check needed.
        if (string.IsNullOrWhiteSpace(design.PreviewStoragePath)) return NotFound();

        var abs = _storage.AbsolutePathFor(design.PreviewStoragePath);
        if (!System.IO.File.Exists(abs)) return NotFound();

        var stream = System.IO.File.OpenRead(abs);
        return File(stream, "image/jpeg");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Returns the authenticated user id, or null for anonymous callers.</summary>
    private int? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>
    /// Returns true if the current caller may interact with <paramref name="design"/>.
    /// Anonymous designs (UserId == null) are accessible by anyone — the only hard gate
    /// is at order-creation time (which requires auth). Authenticated designs require
    /// a matching userId or an Admin role.
    /// </summary>
    private bool UserCanAccess(BannerDesign design, int? userId)
    {
        if (design.UserId is null) return true;          // anonymous design — open
        if (design.UserId == userId) return true;        // owner
        return User.IsInRole(nameof(UserRole.Admin));    // admin override
    }

    private string? GetClientIpAddress()
    {
        var forwarded = Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',')[0].Trim();
            if (!string.IsNullOrEmpty(first)) return first;
        }
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    private static async Task<bool> VerifyMagicBytesAsync(IFormFile file, string ext, CancellationToken ct)
    {
        // Read up to 16 bytes to cover all signatures of interest.
        var head = new byte[16];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(head.AsMemory(), ct);
        if (read < 4) return false;

        var sigs = ext.ToLowerInvariant() switch
        {
            "jpg"  => JpegSigs,
            "jpeg" => JpegSigs,
            "png"  => PngSigs,
            "webp" => WebpSigs,
            "pdf"  => PdfSigs,
            _ => null
        };
        if (sigs is null) return false;

        foreach (var (sig, offset) in sigs)
        {
            if (read < offset + sig.Length) return false;
            for (int i = 0; i < sig.Length; i++)
                if (head[offset + i] != sig[i]) return false;
        }
        return true;
    }
}
