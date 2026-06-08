using BannerShop.Api.Models.DesignRequests;
using BannerShop.Api.Services.AiCredits;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.Email;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace BannerShop.Api.Services.DesignRequests;

public sealed class DesignRequestService : IDesignRequestService
{
    public const decimal AiPriceNok = 95m;
    public const decimal ManualPriceNok = 495m;

    private readonly BannerShopDbContext _db;
    private readonly IStripePaymentService _stripe;
    private readonly IDesignRequestJobQueue _queue;
    private readonly BannerFileStorage _storage;
    private readonly IImageProcessingService _images;
    private readonly IEmailService _email;
    private readonly IAiCreditService _credits;
    private readonly ILogger<DesignRequestService> _log;

    public DesignRequestService(
        BannerShopDbContext db,
        IStripePaymentService stripe,
        IDesignRequestJobQueue queue,
        BannerFileStorage storage,
        IImageProcessingService images,
        IEmailService email,
        IAiCreditService credits,
        ILogger<DesignRequestService> log)
    {
        _db = db;
        _stripe = stripe;
        _queue = queue;
        _storage = storage;
        _images = images;
        _email = email;
        _credits = credits;
        _log = log;
    }

    public async Task<DesignRequestActionResult> CreateAiRequestAsync(int userId, CreateAiDesignRequestDto req, CancellationToken ct = default)
    {
        var template = await _db.BannerTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == req.TemplateId, ct);
        if (template is null)
            return DesignRequestActionResult.Fail("Banner template not found.");

        string? uploadedPhotoPath = null;
        if (req.UploadedPhotoBannerDesignId is int designId)
        {
            var design = await _db.BannerDesigns.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == designId && d.UserId == userId, ct);
            if (design is null)
                return DesignRequestActionResult.Fail("Uploaded photo not found.");
            uploadedPhotoPath = design.StoragePath;
        }

        var request = new DesignRequest
        {
            UserId = userId,
            BannerTemplateId = template.Id,
            Mode = DesignRequestMode.Ai,
            Language = req.Language,
            PersonName = req.PersonName.Trim(),
            PersonAge = req.PersonAge,
            TextContent = req.TextContent.Trim(),
            ThemeDescription = req.ThemeDescription.Trim(),
            UploadedPhotoPath = uploadedPhotoPath,
            AspectRatio = req.AspectRatio,
            Status = DesignRequestStatus.Pending,
            PriceNok = AiPriceNok,
            RegenerationsRemaining = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.DesignRequests.Add(request);
        await _db.SaveChangesAsync(ct);

        // Use the request id as the "order id" metadata field so Stripe webhook handlers
        // can distinguish design-request payments by looking up PaymentIntentId in either
        // the Orders table or the DesignRequests table — see WebhooksController.
        var intent = await _stripe.CreatePaymentIntentAsync(
            orderId: -request.Id,   // negative => not an Order; see WebhooksController.
            userId: userId,
            amountNok: AiPriceNok,
            ct: ct);

        request.StripePaymentIntentId = intent.PaymentIntentId;
        await _db.SaveChangesAsync(ct);

        return DesignRequestActionResult.Ok(request.Id, intent.ClientSecret, AiPriceNok);
    }

    public async Task<DesignRequestActionResult> CreateManualRequestAsync(int userId, CreateManualDesignRequestDto req, CancellationToken ct = default)
    {
        var template = await _db.BannerTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == req.TemplateId, ct);
        if (template is null)
            return DesignRequestActionResult.Fail("Banner template not found.");

        string? uploadedPhotoPath = null;
        if (req.UploadedPhotoBannerDesignId is int designId)
        {
            var design = await _db.BannerDesigns.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == designId && d.UserId == userId, ct);
            if (design is null)
                return DesignRequestActionResult.Fail("Uploaded photo not found.");
            uploadedPhotoPath = design.StoragePath;
        }

        var request = new DesignRequest
        {
            UserId = userId,
            BannerTemplateId = template.Id,
            Mode = DesignRequestMode.Manual,
            Language = req.Language,
            PersonName = req.PersonName.Trim(),
            PersonAge = req.PersonAge,
            TextContent = req.TextContent.Trim(),
            ThemeDescription = req.ThemeDescription.Trim(),
            UploadedPhotoPath = uploadedPhotoPath,
            AspectRatio = req.AspectRatio,
            Status = DesignRequestStatus.Pending,
            PriceNok = ManualPriceNok,
            RegenerationsRemaining = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.DesignRequests.Add(request);
        await _db.SaveChangesAsync(ct);

        var intent = await _stripe.CreatePaymentIntentAsync(
            orderId: -request.Id,
            userId: userId,
            amountNok: ManualPriceNok,
            ct: ct);

        request.StripePaymentIntentId = intent.PaymentIntentId;
        await _db.SaveChangesAsync(ct);

        return DesignRequestActionResult.Ok(request.Id, intent.ClientSecret, ManualPriceNok);
    }

    public async Task<DesignRequestActionResult> RequestRevisionAsync(int id, int callerUserId, string comment, CancellationToken ct = default)
    {
        var r = await _db.DesignRequests
            .Include(x => x.User)
            .Include(x => x.Revisions)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (r is null) return DesignRequestActionResult.Fail("Design request not found.");
        if (r.UserId != callerUserId) return DesignRequestActionResult.Fail("Forbidden.");

        if (r.Status != DesignRequestStatus.AwaitingApproval)
            return DesignRequestActionResult.Fail($"Cannot request a revision from status {r.Status}.");

        if (r.Mode != DesignRequestMode.Manual)
            return DesignRequestActionResult.Fail("Revisions are only available for manual design requests.");

        if (r.RevisionCount >= 1)
            return DesignRequestActionResult.Fail("No free revisions remaining on this request.");

        r.Revisions.Add(new DesignRequestRevision
        {
            DesignRequestId = r.Id,
            RevisionNumber = r.RevisionCount + 1,
            CustomerComment = comment.Trim(),
            CreatedAt = DateTime.UtcNow
        });

        r.RevisionCount++;
        r.Status = DesignRequestStatus.RevisionRequested;
        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return DesignRequestActionResult.Ok(ToDetail(r));
    }

    public async Task<IReadOnlyList<DesignRequestListItemDto>> ListMineAsync(int userId, CancellationToken ct = default)
    {
        var rows = await _db.DesignRequests.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new DesignRequestListItemDto
            {
                Id = r.Id,
                BannerTemplateId = r.BannerTemplateId,
                Mode = r.Mode.ToString(),
                Status = r.Status.ToString(),
                AspectRatio = r.AspectRatio,
                PriceNok = r.PriceNok,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            })
            .ToListAsync(ct);
        return rows;
    }

    public async Task<DesignRequestDetailDto?> GetAsync(int id, int callerUserId, bool isAdmin, CancellationToken ct = default)
    {
        var r = await _db.DesignRequests
            .AsNoTracking()
            .Include(x => x.Revisions.OrderBy(rv => rv.RevisionNumber))
            .Include(x => x.Generations.OrderBy(g => g.CreatedAt))
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return null;
        if (r.UserId != callerUserId && !isAdmin) return null;
        return ToDetail(r);
    }

    public async Task<DesignRequestActionResult> ApproveAsync(int id, int callerUserId, CancellationToken ct = default)
    {
        var r = await _db.DesignRequests
            .Include(x => x.Revisions)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return DesignRequestActionResult.Fail("Design request not found.");
        if (r.UserId != callerUserId)
            return DesignRequestActionResult.Fail("Forbidden.");

        if (r.Status is not (DesignRequestStatus.AwaitingApproval or DesignRequestStatus.Revised))
            return DesignRequestActionResult.Fail($"Cannot approve a request in status {r.Status}.");

        r.Status = DesignRequestStatus.Final;
        r.CustomerApprovedAt = DateTime.UtcNow;
        r.UpdatedAt = DateTime.UtcNow;

        // Create a BannerDesign row so the customer can add the result directly to the print cart.
        await TryCreateFinalBannerDesignAsync(r, ct);

        await _db.SaveChangesAsync(ct);
        return DesignRequestActionResult.Ok(ToDetail(r));
    }

    public async Task<RegenerateResult> RegenerateAsync(int id, int callerUserId, RegenerateAiRequestDto req, CancellationToken ct = default)
    {
        var r = await _db.DesignRequests
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (r is null) return RegenerateResult.Fail("Design request not found.", 404);
        if (r.UserId != callerUserId) return RegenerateResult.Fail("Forbidden.", 403);
        if (r.Mode != DesignRequestMode.Ai)
            return RegenerateResult.Fail("Regenerate is only available for AI design requests.");

        // Allow regenerate from AwaitingApproval (most common), InProgress (retry after failure), or Failed.
        if (r.Status is not (DesignRequestStatus.AwaitingApproval
                          or DesignRequestStatus.InProgress
                          or DesignRequestStatus.Failed))
        {
            return RegenerateResult.Fail($"Cannot regenerate from status {r.Status}.");
        }

        // Consume 1 credit — returns false if insufficient.
        var consumed = await _credits.TryConsumeAsync(callerUserId, count: 1, ct);
        if (!consumed)
        {
            var balance = await _credits.GetBalanceAsync(callerUserId, ct);
            return RegenerateResult.Paywall(balance, new
            {
                reason = "insufficient_credits"
            });
        }

        // Apply mutable input overrides.
        if (!string.IsNullOrWhiteSpace(req.TextContent))
            r.TextContent = req.TextContent.Trim();
        if (!string.IsNullOrWhiteSpace(req.ThemeDescription))
            r.ThemeDescription = req.ThemeDescription.Trim();

        // Pre-create a BannerGeneration row so we can return the ID immediately.
        var generation = new BannerGeneration
        {
            DesignRequestId = r.Id,
            Status = BannerGenerationStatus.Pending,
            IsActive = false,   // pipeline sets it active when it starts
            CreatedAt = DateTime.UtcNow
        };
        _db.BannerGenerations.Add(generation);

        // Reset status so the pipeline's guard doesn't skip.
        r.Status = DesignRequestStatus.InProgress;
        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Enqueue for the background processor.
        await _queue.EnqueueAsync(r.Id, ct);

        var creditsRemaining = await _credits.GetBalanceAsync(callerUserId, ct);
        _log.LogInformation("RegenerateAsync: enqueued generation {GenId} for DesignRequest {Id} (credits left: {Credits})",
            generation.Id, r.Id, creditsRemaining);

        return RegenerateResult.Ok(generation.Id, creditsRemaining);
    }

    public async Task MarkPaidAndEnqueueAsync(string paymentIntentId, CancellationToken ct = default)
    {
        var request = await _db.DesignRequests
            .FirstOrDefaultAsync(r => r.StripePaymentIntentId == paymentIntentId, ct);
        if (request is null)
        {
            _log.LogDebug("No design request found for PI {Pi} — webhook for an Order.", paymentIntentId);
            return;
        }

        // Idempotency — webhook can fire repeatedly.
        if (request.Status is DesignRequestStatus.InProgress
                            or DesignRequestStatus.AwaitingApproval
                            or DesignRequestStatus.Approved
                            or DesignRequestStatus.Final)
        {
            return;
        }

        request.Status = DesignRequestStatus.InProgress;
        request.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (request.Mode == DesignRequestMode.Ai)
        {
            await _queue.EnqueueAsync(request.Id, ct);
            _log.LogInformation("Enqueued AI generation for DesignRequest {Id} (PI {Pi})", request.Id, paymentIntentId);
        }
        else
        {
            // Manual mode — designer will handle it; optionally notify admin.
            _log.LogInformation("Manual DesignRequest {Id} paid (PI {Pi}) — InProgress, awaiting designer.", request.Id, paymentIntentId);
        }
    }

    /// <summary>
    /// If the design request has a final image asset and no BannerDesign row yet,
    /// creates one so the customer can add the design to the print cart.
    /// The caller is responsible for calling <c>SaveChangesAsync</c> afterwards.
    /// </summary>
    internal async Task TryCreateFinalBannerDesignAsync(DesignRequest r, CancellationToken ct)
    {
        if (r.FinalBannerDesignId.HasValue)
            return; // Already created — idempotent.

        var finalPath = r.FinalCroppedStoragePath ?? r.DesignerPreviewPath;
        if (string.IsNullOrEmpty(finalPath))
        {
            _log.LogWarning(
                "TryCreateFinalBannerDesignAsync: DesignRequest {Id} has no final asset path — skipping BannerDesign creation.",
                r.Id);
            return;
        }

        var absPath = _storage.AbsolutePathFor(finalPath);
        if (!File.Exists(absPath))
        {
            _log.LogWarning(
                "TryCreateFinalBannerDesignAsync: file {Path} not found — skipping BannerDesign creation for DesignRequest {Id}.",
                absPath, r.Id);
            return;
        }

        int widthPx, heightPx;
        try
        {
            (widthPx, heightPx) = await _images.ReadDimensionsAsync(absPath, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "TryCreateFinalBannerDesignAsync: failed to read dimensions from {Path} for DesignRequest {Id}.",
                absPath, r.Id);
            return;
        }

        // 18:9 (2:1) aspect ratio → 180 cm tall banner; all others → 150 cm.
        var selectedHeightCm = r.AspectRatio == "18:9" ? 180 : 150;
        var computedWidthCm  = BannerDimensions.ComputeWidthCm(widthPx, heightPx, rotationDegrees: 0, selectedHeightCm);

        // Derive a display name from the storage path.
        var originalFileName = Path.GetFileName(finalPath);
        var contentType = Path.GetExtension(finalPath).TrimStart('.').ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "webp"          => "image/webp",
            _               => "image/png"
        };

        var design = new BannerDesign
        {
            UserId           = r.UserId,
            OriginalFileName = originalFileName,
            StoragePath      = finalPath,
            ContentType      = contentType,
            WidthPx          = widthPx,
            HeightPx         = heightPx,
            RotationDegrees  = 0,
            SelectedHeightCm = selectedHeightCm,
            ComputedWidthCm  = computedWidthCm,
            PreviewStoragePath = null, // customer has already seen the preview
            CreatedAt        = DateTime.UtcNow
        };
        _db.BannerDesigns.Add(design);

        // Flush now to obtain the generated Id before the caller saves the parent entity.
        await _db.SaveChangesAsync(ct);

        r.FinalBannerDesignId = design.Id;
        _log.LogInformation(
            "Created BannerDesign {DesignId} for DesignRequest {RequestId} (path={Path}).",
            design.Id, r.Id, finalPath);
    }

    internal DesignRequestDetailDto ToDetail(DesignRequest r)
    {
        // Prefer the cropped print-ready asset; fall back to the raw AI result.
        var publicPath = r.FinalCroppedStoragePath ?? r.AiResultStoragePath;
        var previewPath = r.DesignerPreviewPath ?? publicPath;

        return new DesignRequestDetailDto
        {
            Id = r.Id,
            UserId = r.UserId,
            BannerTemplateId = r.BannerTemplateId,
            Mode = r.Mode.ToString(),
            Status = r.Status.ToString(),
            Language = r.Language,
            PersonName = r.PersonName,
            PersonAge = r.PersonAge,
            TextContent = r.TextContent,
            ThemeDescription = r.ThemeDescription,
            AspectRatio = r.AspectRatio,
            RevisionCount = r.RevisionCount,
            RegenerationsRemaining = r.RegenerationsRemaining,
            PriceNok = r.PriceNok,
            StripePaymentIntentId = r.StripePaymentIntentId,
            PreviewUrl = string.IsNullOrEmpty(previewPath) ? null : _storage.PublicUrlFor(previewPath),
            FinalCroppedUrl = string.IsNullOrEmpty(r.FinalCroppedStoragePath) ? null : _storage.PublicUrlFor(r.FinalCroppedStoragePath),
            FinalBannerDesignId = r.FinalBannerDesignId,
            CurrentGenerationId = r.CurrentGenerationId,
            LastError = r.LastError,
            CustomerApprovedAt = r.CustomerApprovedAt,
            DesignerNotes = r.DesignerNotes,
            Revisions = r.Revisions
                .OrderBy(rv => rv.RevisionNumber)
                .Select(rv => new DesignRequestRevisionDto
                {
                    Id = rv.Id,
                    RevisionNumber = rv.RevisionNumber,
                    CustomerComment = rv.CustomerComment,
                    CreatedAt = rv.CreatedAt
                })
                .ToList(),
            GenerationHistory = r.Generations
                .OrderBy(g => g.CreatedAt)
                .Select(g => new BannerGenerationHistoryItemDto
                {
                    Id = g.Id,
                    Status = g.Status.ToString(),
                    IsActive = g.IsActive,
                    CreatedAt = g.CreatedAt,
                    CompletedAt = g.CompletedAt
                })
                .ToList(),
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        };
    }
}
