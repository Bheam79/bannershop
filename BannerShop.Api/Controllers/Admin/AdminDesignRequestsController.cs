using BannerShop.Api.Models.DesignRequests;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.DesignRequests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BannerShop.Api.Controllers.Admin;

/// <summary>
/// Admin endpoints for reviewing and managing AI and Manual design requests.
/// All routes require Admin role.
/// </summary>
[ApiController]
[Route("api/admin/design-requests")]
[Authorize(Roles = "Admin")]
public class AdminDesignRequestsController : ControllerBase
{
    private readonly IAdminDesignRequestService _service;
    private readonly BannerFileStorage _storage;
    private readonly ILogger<AdminDesignRequestsController> _log;

    // Max preview upload size: 20 MB
    private const long MaxPreviewBytes = 20L * 1024 * 1024;

    public AdminDesignRequestsController(
        IAdminDesignRequestService service,
        BannerFileStorage storage,
        ILogger<AdminDesignRequestsController> log)
    {
        _service = service;
        _storage = storage;
        _log = log;
    }

    // ── GET /api/admin/design-requests ───────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status = null,
        [FromQuery] string? mode = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _service.ListAsync(new AdminDesignRequestFilter
        {
            Status = status,
            Mode = mode,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Search = search,
            Page = page,
            PageSize = pageSize
        }, ct);
        return Ok(result);
    }

    // ── GET /api/admin/design-requests/{id} ──────────────────────────────────
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var dto = await _service.GetDetailAsync(id, ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    // ── POST /api/admin/design-requests/{id}/upload-preview ──────────────────
    [HttpPost("{id:int}/upload-preview")]
    [RequestSizeLimit(25 * 1024 * 1024)] // 25 MB limit (a little above MaxPreviewBytes)
    [RequestFormLimits(MultipartBodyLengthLimit = 25L * 1024 * 1024)]
    public async Task<IActionResult> UploadPreview(int id, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        if (file.Length > MaxPreviewBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new { error = "File too large (max 20 MB)." });

        var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png" };
        if (!allowedTypes.Contains(file.ContentType?.ToLowerInvariant()))
            return BadRequest(new { error = "Only JPEG and PNG previews are accepted." });

        // Save to design-previews sub-folder
        const string subFolder = "design-previews";
        var dir = Path.Combine(_storage.AbsolutePathFor(subFolder));
        Directory.CreateDirectory(dir);

        var ext = file.ContentType?.Contains("png", StringComparison.OrdinalIgnoreCase) == true ? "png" : "jpg";
        var fileName = $"{Guid.NewGuid():N}.{ext}";
        var absPath = Path.Combine(dir, fileName);

        await using (var fs = System.IO.File.Create(absPath))
        {
            await file.CopyToAsync(fs, ct);
        }

        var storagePath = $"{subFolder}/{fileName}";

        var result = await _service.UploadPreviewAsync(id, storagePath, ct);
        if (!result.Success)
        {
            // Clean up the saved file if the service call fails
            try { System.IO.File.Delete(absPath); } catch { /* best effort */ }
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Detail);
    }

    // ── PUT /api/admin/design-requests/{id}/status ───────────────────────────
    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] AdminUpdateStatusDto req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var result = await _service.UpdateStatusAsync(id, req.Status, req.Notes, ct);
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(result.Detail);
    }
}
