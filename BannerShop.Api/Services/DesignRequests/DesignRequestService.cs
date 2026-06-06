using BannerShop.Api.Models.DesignRequests;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Services.DesignRequests;

public sealed class DesignRequestService : IDesignRequestService
{
    public const decimal AiPriceNok = 95m;

    private readonly BannerShopDbContext _db;
    private readonly IStripePaymentService _stripe;
    private readonly IDesignRequestJobQueue _queue;
    private readonly BannerFileStorage _storage;
    private readonly ILogger<DesignRequestService> _log;

    public DesignRequestService(
        BannerShopDbContext db,
        IStripePaymentService stripe,
        IDesignRequestJobQueue queue,
        BannerFileStorage storage,
        ILogger<DesignRequestService> log)
    {
        _db = db;
        _stripe = stripe;
        _queue = queue;
        _storage = storage;
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
        var r = await _db.DesignRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return null;
        if (r.UserId != callerUserId && !isAdmin) return null;
        return ToDetail(r);
    }

    public async Task<DesignRequestActionResult> ApproveAsync(int id, int callerUserId, CancellationToken ct = default)
    {
        var r = await _db.DesignRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return DesignRequestActionResult.Fail("Design request not found.");
        if (r.UserId != callerUserId)
            return DesignRequestActionResult.Fail("Forbidden.");

        if (r.Status is not (DesignRequestStatus.AwaitingApproval or DesignRequestStatus.Revised))
            return DesignRequestActionResult.Fail($"Cannot approve a request in status {r.Status}.");

        r.Status = DesignRequestStatus.Approved;
        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return DesignRequestActionResult.Ok(ToDetail(r));
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

        await _queue.EnqueueAsync(request.Id, ct);
        _log.LogInformation("Enqueued AI generation for DesignRequest {Id} (PI {Pi})", request.Id, paymentIntentId);
    }

    private DesignRequestDetailDto ToDetail(DesignRequest r)
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
            LastError = r.LastError,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        };
    }
}
