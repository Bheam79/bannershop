using BannerShop.Api.Models.DesignRequests;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.DesignRequests.Replicate;
using BannerShop.Api.Services.Email;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Services.DesignRequests;

public sealed class AdminDesignRequestService : IAdminDesignRequestService
{
    private readonly BannerShopDbContext _db;
    private readonly BannerFileStorage _storage;
    private readonly IEmailService _email;
    private readonly DesignRequestService _base;
    private readonly ILogger<AdminDesignRequestService> _log;
    // Optional: only resolved when Replicate is configured (see Program.cs).
    private readonly RealEsrganUpscalingService? _upscaler;

    public AdminDesignRequestService(
        BannerShopDbContext db,
        BannerFileStorage storage,
        IEmailService email,
        DesignRequestService baseService,
        ILogger<AdminDesignRequestService> log,
        RealEsrganUpscalingService? upscaler = null)
    {
        _db = db;
        _storage = storage;
        _email = email;
        _base = baseService;
        _log = log;
        _upscaler = upscaler;
    }

    // ── List ──────────────────────────────────────────────────────────────────

    public async Task<PagedResult<AdminDesignRequestListItemDto>> ListAsync(
        AdminDesignRequestFilter filter, CancellationToken ct = default)
    {
        var q = _db.DesignRequests
            .AsNoTracking()
            .Include(r => r.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Status) &&
            Enum.TryParse<DesignRequestStatus>(filter.Status, ignoreCase: true, out var statusEnum))
        {
            q = q.Where(r => r.Status == statusEnum);
        }

        if (!string.IsNullOrWhiteSpace(filter.Mode) &&
            Enum.TryParse<DesignRequestMode>(filter.Mode, ignoreCase: true, out var modeEnum))
        {
            q = q.Where(r => r.Mode == modeEnum);
        }

        if (filter.FromUtc.HasValue)
            q = q.Where(r => r.CreatedAt >= filter.FromUtc.Value);

        if (filter.ToUtc.HasValue)
            q = q.Where(r => r.CreatedAt <= filter.ToUtc.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.ToLower();
            q = q.Where(r =>
                r.PersonName.ToLower().Contains(s) ||
                r.User.Name.ToLower().Contains(s) ||
                r.User.Email.ToLower().Contains(s));
        }

        var total = await q.CountAsync(ct);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var page = Math.Max(filter.Page, 1);

        var items = await q
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new AdminDesignRequestListItemDto
            {
                Id = r.Id,
                Mode = r.Mode.ToString(),
                Status = r.Status.ToString(),
                AspectRatio = r.AspectRatio,
                PriceNok = r.PriceNok,
                BannerTemplateId = r.BannerTemplateId,
                PersonName = r.PersonName,
                PersonAge = r.PersonAge,
                UserId = r.UserId,
                CustomerName = r.User.Name,
                CustomerEmail = r.User.Email,
                RevisionCount = r.RevisionCount,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            })
            .ToListAsync(ct);

        return new PagedResult<AdminDesignRequestListItemDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    // ── Detail ────────────────────────────────────────────────────────────────

    public async Task<AdminDesignRequestDetailDto?> GetDetailAsync(int id, CancellationToken ct = default)
    {
        var r = await _db.DesignRequests
            .AsNoTracking()
            .Include(x => x.User)
            .Include(x => x.BannerTemplate)
            .Include(x => x.Revisions.OrderBy(rv => rv.RevisionNumber))
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (r is null) return null;

        var baseDetail = _base.ToDetail(r);
        var photoUrl = string.IsNullOrEmpty(r.UploadedPhotoPath)
            ? null
            : _storage.PublicUrlFor(r.UploadedPhotoPath);

        return new AdminDesignRequestDetailDto
        {
            // copy base fields
            Id = baseDetail.Id,
            UserId = baseDetail.UserId,
            BannerTemplateId = baseDetail.BannerTemplateId,
            Mode = baseDetail.Mode,
            Status = baseDetail.Status,
            Language = baseDetail.Language,
            PersonName = baseDetail.PersonName,
            PersonAge = baseDetail.PersonAge,
            TextContent = baseDetail.TextContent,
            ThemeDescription = baseDetail.ThemeDescription,
            AspectRatio = baseDetail.AspectRatio,
            RevisionCount = baseDetail.RevisionCount,
            RegenerationsRemaining = baseDetail.RegenerationsRemaining,
            PriceNok = baseDetail.PriceNok,
            StripePaymentIntentId = baseDetail.StripePaymentIntentId,
            PreviewUrl = baseDetail.PreviewUrl,
            FinalCroppedUrl = baseDetail.FinalCroppedUrl,
            LastError = baseDetail.LastError,
            CustomerApprovedAt = baseDetail.CustomerApprovedAt,
            DesignerNotes = baseDetail.DesignerNotes,
            Revisions = baseDetail.Revisions,
            CreatedAt = baseDetail.CreatedAt,
            UpdatedAt = baseDetail.UpdatedAt,
            // admin-only extras
            CustomerName = r.User.Name,
            CustomerEmail = r.User.Email,
            UploadedPhotoUrl = photoUrl,
            TemplateName = r.BannerTemplate.NameNb,
        };
    }

    // ── Upload preview (admin sets designer result) ───────────────────────────

    public async Task<DesignRequestActionResult> UploadPreviewAsync(
        int id, string storagePath, CancellationToken ct = default)
    {
        var r = await _db.DesignRequests
            .Include(x => x.User)
            .Include(x => x.Revisions)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (r is null) return DesignRequestActionResult.Fail("Design request not found.");

        r.DesignerPreviewPath = storagePath;
        r.Status = DesignRequestStatus.AwaitingApproval;
        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Admin uploaded preview for DesignRequest {Id}", id);

        // Notify customer
        if (!string.IsNullOrWhiteSpace(r.User?.Email))
        {
            var subject = "Designet ditt er klart til godkjenning";
            var body = $"""
                <p>Hei {r.User.Name},</p>
                <p>Banneret ditt er nå klart til gjennomgang. Logg inn for å se forhåndsvisningen og godkjenne.</p>
                <p>Med vennlig hilsen,<br>BannerShop</p>
                """;
            try
            {
                await _email.SendAsync(r.User.Email, subject, body, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to send AwaitingApproval email to {Email}", r.User.Email);
            }
        }

        return DesignRequestActionResult.Ok(_base.ToDetail(r));
    }

    // ── Update status (admin override) ────────────────────────────────────────

    public async Task<DesignRequestActionResult> UpdateStatusAsync(
        int id, string status, string? notes, CancellationToken ct = default)
    {
        if (!Enum.TryParse<DesignRequestStatus>(status, ignoreCase: true, out var newStatus))
            return DesignRequestActionResult.Fail($"Unknown status '{status}'.");

        // Restrict to statuses an admin may set directly
        var allowed = new[]
        {
            DesignRequestStatus.InProgress,
            DesignRequestStatus.AwaitingApproval,
            DesignRequestStatus.Revised,
            DesignRequestStatus.Final,
            DesignRequestStatus.Cancelled
        };
        if (!allowed.Contains(newStatus))
            return DesignRequestActionResult.Fail($"Status '{status}' cannot be set via this endpoint.");

        var r = await _db.DesignRequests
            .Include(x => x.Revisions)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return DesignRequestActionResult.Fail("Design request not found.");

        r.Status = newStatus;
        r.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(notes))
            r.DesignerNotes = notes.Trim();

        // When the admin pushes to Final, ensure a BannerDesign row exists so the
        // customer can add the result to their print cart.
        if (newStatus == DesignRequestStatus.Final)
            await _base.TryCreateFinalBannerDesignAsync(r, ct);

        await _db.SaveChangesAsync(ct);
        return DesignRequestActionResult.Ok(_base.ToDetail(r));
    }

    // ── 4x Real-ESRGAN upscale (BANNERSH-57) ──────────────────────────────────

    public async Task<DesignRequestActionResult> UpscaleFinalAsync(int id, int scale, CancellationToken ct = default)
    {
        if (_upscaler is null)
            return DesignRequestActionResult.Fail(
                "Real-ESRGAN upscaler is not configured. Set Replicate:ApiToken in appsettings.");

        if (scale is not (2 or 4))
            return DesignRequestActionResult.Fail("Scale must be 2 or 4.");

        var r = await _db.DesignRequests
            .Include(x => x.Revisions)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return DesignRequestActionResult.Fail("Design request not found.");

        // Prefer the cropped print-ready file; fall back to the raw AI output.
        var sourceRelative = !string.IsNullOrWhiteSpace(r.FinalCroppedStoragePath)
            ? r.FinalCroppedStoragePath!
            : r.AiResultStoragePath;
        if (string.IsNullOrWhiteSpace(sourceRelative))
            return DesignRequestActionResult.Fail("Design request has no image to upscale yet.");

        var sourceAbs = _storage.AbsolutePathFor(sourceRelative);
        if (!File.Exists(sourceAbs))
            return DesignRequestActionResult.Fail($"Source image missing on disk: {sourceRelative}");

        string upscaledTempPath;
        try
        {
            upscaledTempPath = await _upscaler.UpscaleAsync(sourceAbs, scale, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Real-ESRGAN upscale failed for DesignRequest {Id}", id);
            return DesignRequestActionResult.Fail($"Upscale failed: {ex.Message}");
        }

        // Save into the customer's storage folder as a new file (don't clobber the
        // original — production may want both for comparison).
        var userDir = _storage.EnsureUserDirectory(r.UserId);
        var ext = Path.GetExtension(upscaledTempPath);
        if (string.IsNullOrEmpty(ext)) ext = ".png";
        var fileName = $"design_{r.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}_x{scale}{ext}";
        var destAbs = Path.Combine(userDir, fileName);
        try
        {
            File.Move(upscaledTempPath, destAbs, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to persist upscaled image for DesignRequest {Id}", id);
            try { File.Delete(upscaledTempPath); } catch { /* best effort */ }
            return DesignRequestActionResult.Fail($"Could not persist upscaled file: {ex.Message}");
        }

        var newRelative = BannerFileStorage.RelativePathFor(r.UserId, fileName);
        r.FinalCroppedStoragePath = newRelative;
        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("DesignRequest {Id}: FinalCroppedStoragePath upscaled x{Scale} -> {Path}",
            r.Id, scale, newRelative);

        return DesignRequestActionResult.Ok(_base.ToDetail(r));
    }
}
