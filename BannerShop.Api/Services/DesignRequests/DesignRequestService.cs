using BannerShop.Api.Models.DesignRequests;
using BannerShop.Api.Services.AiCredits;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.Email;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Core.Helpers;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace BannerShop.Api.Services.DesignRequests;

public sealed class DesignRequestService : IDesignRequestService
{
    // The 95 kr standalone AI fee retired in BANNERSH-67 — the 95 kr is now collected
    // as an AI activation line item on the print order (BANNERSH-68).
    public const decimal ManualPriceNok = 495m;

    private readonly BannerShopDbContext _db;
    private readonly IStripePaymentService _stripe;
    private readonly IDesignRequestJobQueue _queue;
    private readonly BannerFileStorage _storage;
    private readonly IImageProcessingService _images;
    private readonly IEmailService _email;
    private readonly IAiCreditService _credits;
    private readonly IPricingService _pricing;
    private readonly ILogger<DesignRequestService> _log;

    public DesignRequestService(
        BannerShopDbContext db,
        IStripePaymentService stripe,
        IDesignRequestJobQueue queue,
        BannerFileStorage storage,
        IImageProcessingService images,
        IEmailService email,
        IAiCreditService credits,
        IPricingService pricing,
        ILogger<DesignRequestService> log)
    {
        _db = db;
        _stripe = stripe;
        _queue = queue;
        _storage = storage;
        _images = images;
        _email = email;
        _credits = credits;
        _pricing = pricing;
        _log = log;
    }

    /// <inheritdoc />
    /// <remarks>
    /// BANNERSH-67: the standalone 95 kr Stripe PaymentIntent is gone. AI generation
    /// is free-first (one per IP for anonymous, one per user for authenticated) and
    /// credit-gated afterwards. Payment is collected later via the print-order's
    /// mandatory AI activation fee (BANNERSH-68).
    /// </remarks>
    public async Task<CreateAiResult> CreateAiRequestAsync(
        int? userId,
        string? ipAddress,
        CreateAiDesignRequestDto req,
        CancellationToken ct = default)
    {
        var template = await _db.BannerTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == req.TemplateId, ct);
        if (template is null)
            return CreateAiResult.Fail("Banner template not found.", 400);

        // Uploaded portraits live in the user's BannerDesigns table — only available
        // for authenticated callers. Silently ignore for anonymous requests.
        string? uploadedPhotoPath = null;
        if (userId is int authUserId && req.UploadedPhotoBannerDesignId is int designId)
        {
            var design = await _db.BannerDesigns.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == designId && d.UserId == authUserId, ct);
            if (design is null)
                return CreateAiResult.Fail("Uploaded photo not found.", 400);
            uploadedPhotoPath = design.StoragePath;
        }

        // ── Anonymous path ───────────────────────────────────────────────────────
        if (userId is null)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return CreateAiResult.Fail("Client IP address could not be determined.", 400);

            var eligible = await _credits.IsAnonymousEligibleAsync(ipAddress, ct);
            if (!eligible)
            {
                var paywall = await BuildPaywallAsync("ip_limit_reached", ct);
                return CreateAiResult.PaywallResult(paywall, 0);
            }

            var anonRequest = await PersistAndEnqueueAsync(
                userId: null,
                ipAddress: ipAddress,
                template: template,
                uploadedPhotoPath: null,   // no portrait upload for anonymous
                regenerationsRemaining: 0,
                req: req,
                ct);

            await _credits.RecordAnonymousUsageAsync(ipAddress, ct);

            // Anonymous callers can generate but cannot approve / continue without an account.
            return CreateAiResult.Ok(anonRequest.Id, requiresAuth: true, creditsRemaining: 0);
        }

        // ── Authenticated path ───────────────────────────────────────────────────
        var uid = userId.Value;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid, ct);
        if (user is null)
            return CreateAiResult.Fail("User not found.", 404);

        if (!user.HasUsedFreeAiGeneration)
        {
            // First-ever generation for this user — free, no credit consumed.
            user.HasUsedFreeAiGeneration = true;
            _db.AiCreditTransactions.Add(new AiCreditTransaction
            {
                UserId = uid,
                Amount = 0,                 // no credit movement — audit row only
                Reason = CreditReason.FreeAuthenticated,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            var consumed = await _credits.TryConsumeAsync(uid, count: 1, ct);
            if (!consumed)
            {
                var paywall = await BuildPaywallAsync("insufficient_credits", ct);
                return CreateAiResult.PaywallResult(paywall, 0);
            }
        }

        var authRequest = await PersistAndEnqueueAsync(
            userId: uid,
            ipAddress: null,
            template: template,
            uploadedPhotoPath: uploadedPhotoPath,
            regenerationsRemaining: 1,
            req: req,
            ct);

        var creditsRemaining = await _credits.GetBalanceAsync(uid, ct);
        return CreateAiResult.Ok(authRequest.Id, requiresAuth: false, creditsRemaining: creditsRemaining);
    }

    /// <summary>
    /// Persists the <see cref="DesignRequest"/> row (and, for authenticated callers, a
    /// linked <see cref="Order"/> row), sets the status to <c>InProgress</c>, and enqueues
    /// the AI pipeline job. Used by both the anonymous and authenticated branches of
    /// <see cref="CreateAiRequestAsync"/>.
    /// </summary>
    private async Task<DesignRequest> PersistAndEnqueueAsync(
        int? userId,
        string? ipAddress,
        BannerTemplate template,
        string? uploadedPhotoPath,
        int regenerationsRemaining,
        CreateAiDesignRequestDto req,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Authenticated AI requests get an Order row so the request has a canonical
        // identity for order tracking. Anonymous free-tier requests are left unlinked
        // (no UserId → no Order).
        Order? order = null;
        if (userId.HasValue)
        {
            order = new Order
            {
                UserId = userId.Value,
                OrderType = OrderType.AiBanner,
                // Credits were consumed before this call, so the request is already "paid".
                OrderState = OrderState.Paid,
                Status = OrderStatus.Paid,
                TotalNok = 0m,   // free-first: the print cost comes via the activation fee
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.Orders.Add(order);
        }

        var request = new DesignRequest
        {
            UserId = userId,
            IpAddress = ipAddress,
            BannerTemplateId = template.Id,
            Mode = DesignRequestMode.Ai,
            Language = req.Language,
            PersonName = req.PersonName.Trim(),
            PersonAge = req.PersonAge,
            TextContent = req.TextContent.Trim(),
            ThemeDescription = req.ThemeDescription.Trim(),
            UploadedPhotoPath = uploadedPhotoPath,
            AspectRatio = req.AspectRatio,
            // Pipeline guard rejects terminal statuses; InProgress lets it run immediately.
            Status = DesignRequestStatus.InProgress,
            PriceNok = 0m,                          // free-first: no upfront price
            RegenerationsRemaining = regenerationsRemaining,
            CreatedAt = now,
            UpdatedAt = now
        };
        if (order is not null) request.Order = order;

        _db.DesignRequests.Add(request);
        await _db.SaveChangesAsync(ct);  // Order + DesignRequest saved atomically

        await _queue.EnqueueAsync(request.Id, ct);
        _log.LogInformation(
            "CreateAiRequestAsync: enqueued DesignRequest {Id} (user={UserId}, ip={Ip}, template={TemplateId}, orderId={OrderId}).",
            request.Id, userId, ipAddress, template.Id, order?.Id);

        return request;
    }

    /// <summary>
    /// Loads the four pricing parameters that populate the paywall payload and
    /// wraps them in an <see cref="AiPaywallResponseDto"/>. Falls back to the
    /// defaults seeded by BANNERSH-65 when any are missing.
    /// </summary>
    private async Task<AiPaywallResponseDto> BuildPaywallAsync(string reason, CancellationToken ct)
    {
        var pricing = await _db.PricingParameters.AsNoTracking()
            .Where(p => p.Key == "ai_credit_pack_price_nok"
                     || p.Key == "ai_credit_pack_count"
                     || p.Key == "ai_credit_pack_large_price_nok"
                     || p.Key == "ai_credit_pack_large_count"
                     || p.Key == "ai_banner_activation_fee_nok"
                     || p.Key == "ai_banner_activation_credits")
            .ToDictionaryAsync(p => p.Key, p => p.Value, ct);

        return new AiPaywallResponseDto
        {
            Reason = reason,
            CreditsRemaining = 0,
            PaywallOptions = new PaywallOptions
            {
                CreditPackSmallPriceNok = pricing.GetValueOrDefault("ai_credit_pack_price_nok", 29m),
                CreditPackSmallCount    = (int)pricing.GetValueOrDefault("ai_credit_pack_count", 5m),
                CreditPackLargePriceNok = pricing.GetValueOrDefault("ai_credit_pack_large_price_nok", 95m),
                CreditPackLargeCount    = (int)pricing.GetValueOrDefault("ai_credit_pack_large_count", 20m),
                BannerOrderActivationFeeNok = pricing.GetValueOrDefault("ai_banner_activation_fee_nok", 95m),
                BannerOrderCreditBonus = (int)pricing.GetValueOrDefault("ai_banner_activation_credits", 20m),
                ManualDesignerUrl = "/banner-builder/manual",
                UploadOwnUrl = "/banner-builder"
            }
        };
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

        // BANNERSH-104: resolve the physical-banner production cost from the chosen
        // aspect ratio and charge it alongside the design fee. Falls back to 0 if no
        // BannerSize matches the aspect ratio's dimensions (degraded — the customer
        // would only pay the design fee, same as the pre-104 behaviour).
        var (bannerPriceNok, bannerSizeId, customBannerWidthCm) =
            await ResolveBannerProductionCostAsync(req.AspectRatio, ct);
        var totalNok = ManualPriceNok + bannerPriceNok;
        var now = DateTime.UtcNow;

        // Create the Order row atomically with the DesignRequest — Order starts as Draft
        // (no payment yet). MarkPaidAndEnqueueAsync flips it to Paid on webhook.
        var order = new Order
        {
            UserId = userId,
            OrderType = OrderType.ManualDesign,
            OrderState = OrderState.Draft,
            Status = OrderStatus.Draft,
            TotalNok = totalNok,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Orders.Add(order);

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
            BannerPriceNok = bannerPriceNok,
            BannerSizeId = bannerSizeId,
            CustomBannerWidthCm = customBannerWidthCm,
            RegenerationsRemaining = 0,
            Order = order,   // sets OrderId FK atomically
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.DesignRequests.Add(request);
        await _db.SaveChangesAsync(ct);  // Order + DesignRequest saved in one transaction

        // BANNERSH-136: no Stripe PaymentIntent is created upfront. Payment is collected
        // via the normal cart/checkout pipeline — the frontend adds a banner line + a 495 kr
        // designer-fee line to the cart after this call returns, then routes to /checkout.
        // The MarkPaidAndEnqueueAsync webhook path is kept as a dead-code guard for any
        // legacy in-flight manual PIs (from before this deploy).

        return DesignRequestActionResult.OkNoPayment(
            request.Id,
            totalNok,
            designPriceNok: ManualPriceNok,
            bannerPriceNok: bannerPriceNok);
    }

    /// <summary>
    /// Maps a banner dimension string to a concrete <see cref="BannerSize"/> and returns its
    /// production cost. Accepts legacy '16:9'/'18:9' strings and the new 'WxH' format (e.g. '250x150').
    /// Prefers an exact fixed-width match; falls back to a custom-width size with the same height;
    /// returns 0 / null when no matching size exists so the manual flow degrades to design-fee-only.
    /// </summary>
    private async Task<(decimal PriceNok, int? BannerSizeId, int? CustomWidthCm)> ResolveBannerProductionCostAsync(
        string aspectRatio, CancellationToken ct)
    {
        var (targetWidthCm, targetHeightCm) = ParseDimensions(aspectRatio);

        // Prefer an exact fixed-width match — avoids the custom-width surcharge.
        var exact = await _db.BannerSizes
            .Include(s => s.Material)
            .Where(s => s.IsActive
                     && !s.IsCustomWidth
                     && s.WidthCm == targetWidthCm
                     && s.HeightCm == targetHeightCm)
            .OrderBy(s => s.SortOrder)
            .FirstOrDefaultAsync(ct);
        if (exact is not null)
        {
            var price = await _pricing.CalculatePriceAsync(exact);
            return (decimal.Round(price, 2), exact.Id, null);
        }

        // Fall back to a custom-width size of the same height.
        var custom = await _db.BannerSizes
            .Include(s => s.Material)
            .Where(s => s.IsActive && s.IsCustomWidth && s.HeightCm == targetHeightCm)
            .OrderBy(s => s.SortOrder)
            .FirstOrDefaultAsync(ct);
        if (custom is not null)
        {
            var price = await _pricing.CalculatePriceAsync(custom, targetWidthCm);
            return (decimal.Round(price, 2), custom.Id, targetWidthCm);
        }

        // No matching size — log so admins notice the catalog gap. The manual flow still
        // works, the customer just only pays the design fee (same as pre-104 behaviour).
        _log.LogWarning(
            "ResolveBannerProductionCostAsync: no BannerSize matches aspectRatio={Ratio} ({W}×{H} cm). " +
            "Falling back to design-fee-only pricing.",
            aspectRatio, targetWidthCm, targetHeightCm);
        return (0m, null, null);
    }

    /// <summary>
    /// Parses a dimension string into (widthCm, heightCm).
    /// Accepts '16:9' (→ 266×150), '18:9' (→ 300×150), or 'WxH' format (e.g. '250x150').
    /// Falls back to 250×150 for unrecognised values.
    /// </summary>
    private static (int Width, int Height) ParseDimensions(string aspectRatio)
    {
        if (aspectRatio == "18:9") return (300, 150);
        if (aspectRatio == "16:9") return (266, 150);

        // WxH format: e.g. "250x150", "270x180"
        var sep = aspectRatio.IndexOfAny(['x', 'X']);
        if (sep > 0
            && int.TryParse(aspectRatio[..sep], out var w)
            && int.TryParse(aspectRatio[(sep + 1)..], out var h)
            && w > 0 && h > 0)
        {
            return (w, h);
        }

        return (250, 150); // safe default
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
        // We need the raw paths to resolve the public URL via BannerFileStorage on the server
        // side; project the storage paths down and translate them in-process.
        var rows = await _db.DesignRequests.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id,
                r.BannerTemplateId,
                r.Mode,
                r.Status,
                r.AspectRatio,
                r.PriceNok,
                r.CreatedAt,
                r.UpdatedAt,
                r.PersonName,
                r.ThemeDescription,
                r.DesignerPreviewPath,
                r.FinalCroppedStoragePath,
                r.AiResultStoragePath,
                r.AiPreviewPath
            })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            // Prefer the low-res AI preview (BANNERSH-91) so customers can't easily repurpose
            // the preview for printing. Fall back to the designer path or full-res for older rows.
            var previewPath = r.AiPreviewPath ?? r.DesignerPreviewPath ?? r.FinalCroppedStoragePath ?? r.AiResultStoragePath;
            return new DesignRequestListItemDto
            {
                Id = r.Id,
                BannerTemplateId = r.BannerTemplateId,
                Mode = r.Mode.ToString(),
                Status = r.Status.ToString(),
                AspectRatio = r.AspectRatio,
                PriceNok = r.PriceNok,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                PersonName = r.PersonName,
                ThemeDescription = r.ThemeDescription,
                PreviewUrl = string.IsNullOrEmpty(previewPath) ? null : _storage.PublicUrlFor(previewPath)
            };
        }).ToList();
    }

    public async Task<DesignRequestDetailDto?> GetAsync(int id, int callerUserId, bool isAdmin, CancellationToken ct = default)
    {
        var r = await _db.DesignRequests
            .AsNoTracking()
            .Include(x => x.Revisions.OrderBy(rv => rv.RevisionNumber))
            .Include(x => x.Generations.OrderBy(g => g.CreatedAt))
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return null;
        // Anonymous design requests (UserId=null) are accessible by anyone who knows the id —
        // they are created by un-authed users and contain no secret personal data beyond what
        // the person typed in the form.  The short-lived design-id is treated as the bearer.
        // Authenticated requests are private to their owner (or admin).
        if (r.UserId is not null && r.UserId != callerUserId && !isAdmin) return null;
        return ToDetail(r);
    }

    public async Task<DesignRequestActionResult> ApproveAsync(int id, int callerUserId, int? selectedHeightCm = null, CancellationToken ct = default)
    {
        var r = await _db.DesignRequests
            .Include(x => x.Revisions)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return DesignRequestActionResult.Fail("Design request not found.");
        if (r.UserId != callerUserId)
            return DesignRequestActionResult.Fail("Forbidden.");

        if (r.Status is not (DesignRequestStatus.AwaitingApproval or DesignRequestStatus.Revised))
            return DesignRequestActionResult.Fail($"Cannot approve a request in status {r.Status}.");

        // Move to Approved (customer accepted the preview). Final is set later by admin
        // when the physical banner is delivered.
        r.Status = DesignRequestStatus.Approved;
        r.CustomerApprovedAt = DateTime.UtcNow;
        r.UpdatedAt = DateTime.UtcNow;

        // If the customer chose a specific print height in the quality/size picker, persist it
        // so reorders and admin views also see the correct size (BANNERSH-168).
        if (selectedHeightCm.HasValue && selectedHeightCm.Value > 0)
        {
            // Rewrite AspectRatio to WxH so TryCreateFinalBannerDesignAsync picks up the height.
            // Width is derived from the actual pixel dimensions, so we store a placeholder width
            // that will be recomputed from the image; the height is the authoritative user choice.
            var existingWidth = ParseDimensions(r.AspectRatio ?? "360x150").Width;
            r.AspectRatio = $"{existingWidth}x{selectedHeightCm.Value}";
        }

        // Advance the linked Order state: CustomerApproval → InProduction (BANNERSH-109).
        if (r.OrderId.HasValue)
        {
            var order = await _db.Orders.FindAsync(new object?[] { r.OrderId.Value }, ct);
            if (order is not null
                && OrderStateHelper.IsValidTransition(order.OrderType, order.OrderState, OrderState.InProduction))
            {
                order.OrderState = OrderState.InProduction;
                order.Status = OrderStatus.InProduction;
                order.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Create a BannerDesign row so the customer can add the result directly to the print cart.
        // Pass the customer-chosen height so the print size matches their picker selection.
        await TryCreateFinalBannerDesignAsync(r, ct, overrideHeightCm: selectedHeightCm);

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

        // Allow regenerate from AwaitingApproval / InProgress / Failed (mutate in place)
        // OR from Approved / Final (copy to a new DesignRequest so the original record is
        // preserved for re-ordering / audit).  Any other status is rejected.
        bool isCopy = r.Status is DesignRequestStatus.Approved or DesignRequestStatus.Final;
        if (!isCopy && r.Status is not (DesignRequestStatus.AwaitingApproval
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

        // Resolve optional photo-override (shared logic for both paths).
        string? overridePhotoPath = null;
        bool clearPhoto = false;
        if (req.UploadedPhotoBannerDesignId is int newPhotoId)
        {
            if (newPhotoId < 0)
            {
                clearPhoto = true;
            }
            else
            {
                var photo = await _db.BannerDesigns.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == newPhotoId && d.UserId == callerUserId, ct);
                if (photo is null)
                    return RegenerateResult.Fail("Uploaded photo not found.", 400);
                overridePhotoPath = photo.StoragePath;
            }
        }

        int? newDesignRequestId = null;
        DesignRequest target;

        if (isCopy)
        {
            // Create a fresh DesignRequest that is a copy of the approved/final one, so
            // the original record (with its order history) is not disturbed.
            target = new DesignRequest
            {
                UserId       = r.UserId,
                BannerTemplateId = r.BannerTemplateId,
                Mode         = r.Mode,
                Language     = r.Language,
                PersonName   = !string.IsNullOrWhiteSpace(req.PersonName) ? req.PersonName.Trim() : r.PersonName,
                PersonAge    = req.PersonAge.HasValue ? (req.PersonAge < 0 ? null : req.PersonAge) : r.PersonAge,
                TextContent  = !string.IsNullOrWhiteSpace(req.TextContent) ? req.TextContent.Trim() : r.TextContent,
                ThemeDescription = !string.IsNullOrWhiteSpace(req.ThemeDescription) ? req.ThemeDescription.Trim() : r.ThemeDescription,
                UploadedPhotoPath = clearPhoto ? null : (overridePhotoPath ?? r.UploadedPhotoPath),
                AspectRatio  = r.AspectRatio,
                Status       = DesignRequestStatus.InProgress,
                PriceNok     = 0,   // free-first AI — no Stripe charge upfront
                CreatedAt    = DateTime.UtcNow,
                UpdatedAt    = DateTime.UtcNow,
            };
            _db.DesignRequests.Add(target);
            await _db.SaveChangesAsync(ct);   // materialise to get new Id
            newDesignRequestId = target.Id;
        }
        else
        {
            target = r;

            // Apply mutable input overrides.
            if (!string.IsNullOrWhiteSpace(req.TextContent))
                target.TextContent = req.TextContent.Trim();
            if (!string.IsNullOrWhiteSpace(req.ThemeDescription))
                target.ThemeDescription = req.ThemeDescription.Trim();
            // BANNERSH-84: allow editing person name + age + uploaded photo as part of regenerate.
            if (!string.IsNullOrWhiteSpace(req.PersonName))
                target.PersonName = req.PersonName.Trim();
            if (req.PersonAge is int editedAge)
                target.PersonAge = editedAge < 0 ? null : editedAge;
            if (clearPhoto)
                target.UploadedPhotoPath = null;
            else if (overridePhotoPath is not null)
                target.UploadedPhotoPath = overridePhotoPath;

            // Reset status so the pipeline's guard doesn't skip.
            target.Status = DesignRequestStatus.InProgress;
            target.UpdatedAt = DateTime.UtcNow;
        }

        // Pre-create a BannerGeneration row so we can return the ID immediately.
        var generation = new BannerGeneration
        {
            DesignRequestId = target.Id,
            Status = BannerGenerationStatus.Pending,
            IsActive = false,   // pipeline sets it active when it starts
            CreatedAt = DateTime.UtcNow
        };
        _db.BannerGenerations.Add(generation);

        if (!isCopy)
            target.UpdatedAt = DateTime.UtcNow;   // touch timestamp for in-place path

        await _db.SaveChangesAsync(ct);

        // Enqueue for the background processor.
        await _queue.EnqueueAsync(target.Id, ct);

        var creditsRemaining = await _credits.GetBalanceAsync(callerUserId, ct);
        _log.LogInformation(
            "RegenerateAsync: enqueued generation {GenId} for DesignRequest {Id} (copy={IsCopy}, credits left: {Credits})",
            generation.Id, target.Id, isCopy, creditsRemaining);

        return RegenerateResult.Ok(generation.Id, creditsRemaining, newDesignRequestId);
    }

    /// <inheritdoc />
    /// <remarks>
    /// BANNERSH-84: lets the customer pick a previously-generated version (e.g. they
    /// preferred the first attempt over the latest). Pivots the <see cref="DesignRequest"/>
    /// pointers and the <see cref="BannerGeneration.IsActive"/> flag so the new preview
    /// is what /approve and the past-banners gallery surface. Does not consume a credit.
    /// </remarks>
    public async Task<DesignRequestActionResult> ActivateGenerationAsync(
        int id, int generationId, int callerUserId, CancellationToken ct = default)
    {
        var r = await _db.DesignRequests
            .Include(x => x.Revisions)
            .Include(x => x.Generations)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return DesignRequestActionResult.Fail("Design request not found.");
        if (r.UserId != callerUserId) return DesignRequestActionResult.Fail("Forbidden.");
        if (r.Mode != DesignRequestMode.Ai)
            return DesignRequestActionResult.Fail("Activate is only available for AI design requests.");

        // Only allow swapping while a preview is on the table — Approved/Final lock the choice.
        if (r.Status is not (DesignRequestStatus.AwaitingApproval or DesignRequestStatus.Failed))
            return DesignRequestActionResult.Fail($"Cannot switch active generation from status {r.Status}.");

        var target = r.Generations.FirstOrDefault(g => g.Id == generationId);
        if (target is null) return DesignRequestActionResult.Fail("Generation not found.");
        if (target.Status != BannerGenerationStatus.Completed)
            return DesignRequestActionResult.Fail("Only completed generations can be selected.");
        if (string.IsNullOrEmpty(target.StoragePath))
            return DesignRequestActionResult.Fail("Generation has no image attached.");

        foreach (var g in r.Generations)
            g.IsActive = g.Id == target.Id;

        r.AiResultStoragePath = target.StoragePath;
        r.FinalCroppedStoragePath = target.CroppedStoragePath ?? target.StoragePath;
        r.CurrentGenerationId = target.Id;
        // If the request was Failed, picking a healthy past generation puts it back into AwaitingApproval.
        r.Status = DesignRequestStatus.AwaitingApproval;
        r.LastError = null;
        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return DesignRequestActionResult.Ok(ToDetail(r));
    }

    /// <inheritdoc />
    /// <remarks>
    /// BANNERSH-67 retired the 95 kr standalone AI PaymentIntent — AI generation is
    /// now free-first with payment collected at order time. This handler is kept as a
    /// dead-code guard for any in-flight Manual requests (495 kr designer fee) and
    /// any stale AI PIs from before the deploy.
    /// </remarks>
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

        // Dead-code guard for AI: BANNERSH-67 removed the standalone Stripe PI for AI
        // design. Any payment_intent.succeeded with mode=Ai is an in-flight payment
        // from before the deploy — log + no-op so we don't accidentally double-enqueue.
        if (request.Mode == DesignRequestMode.Ai)
        {
            _log.LogWarning(
                "DesignRequest {Id} (mode=Ai) received a payment_intent.succeeded ({Pi}) after BANNERSH-67 — ignoring (free-first flow no longer creates PIs).",
                request.Id, paymentIntentId);
            return;
        }

        // Manual flow (495 kr) still uses an upfront PaymentIntent — flip to InProgress
        // so the designer dashboard can pick it up.
        request.Status = DesignRequestStatus.InProgress;
        request.UpdatedAt = DateTime.UtcNow;

        // Also advance the linked Order to Paid (BANNERSH-108).
        if (request.OrderId.HasValue)
        {
            var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == request.OrderId.Value, ct);
            if (order is not null)
            {
                order.OrderState = OrderState.Paid;
                order.Status = OrderStatus.Paid;
                order.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Manual DesignRequest {Id} paid (PI {Pi}) — InProgress, awaiting designer.",
            request.Id, paymentIntentId);
    }

    /// <summary>
    /// If the design request has a final image asset and no BannerDesign row yet,
    /// creates one so the customer can add the design to the print cart.
    /// The caller is responsible for calling <c>SaveChangesAsync</c> afterwards.
    /// <para>
    /// Pass <paramref name="overrideHeightCm"/> when the customer has chosen a specific
    /// print height in the quality/size picker (BANNERSH-168). When null the height is
    /// derived from the stored <see cref="DesignRequest.AspectRatio"/> for backward compat.
    /// </para>
    /// </summary>
    internal async Task TryCreateFinalBannerDesignAsync(DesignRequest r, CancellationToken ct, int? overrideHeightCm = null)
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

        // Prefer the customer's explicit picker selection; fall back to the stored AspectRatio.
        // BANNERSH-168: overrideHeightCm carries the user's choice from the quality/size picker
        // (high=150 cm, good=180 cm, custom=user-entered) so the BannerDesign is created at the
        // height the customer actually saw in the UI.
        var selectedHeightCm = (overrideHeightCm is > 0)
            ? overrideHeightCm.Value
            : ParseDimensions(r.AspectRatio ?? "250x150").Height;
        var computedWidthCm  = BannerDimensions.ComputeWidthCm(widthPx, heightPx, rotationDegrees: 0, selectedHeightCm);

        // Derive a display name from the storage path.
        var originalFileName = Path.GetFileName(finalPath);
        var contentType = Path.GetExtension(finalPath).TrimStart('.').ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "webp"          => "image/webp",
            _               => "image/png"
        };

        // BannerDesign.UserId is non-nullable — anonymous DesignRequests never reach
        // Final without going through /approve (auth-required), so this is safe.
        var design = new BannerDesign
        {
            UserId           = r.UserId ?? 0,
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
        // Use the low-res AI preview when available (BANNERSH-91) so customers cannot repurpose
        // the preview for printing. Admin views still expose FinalCroppedUrl at full resolution.
        var previewPath = r.AiPreviewPath ?? r.DesignerPreviewPath ?? publicPath;

        return new DesignRequestDetailDto
        {
            Id = r.Id,
            UserId = r.UserId ?? 0,
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
                    CompletedAt = g.CompletedAt,
                    PreviewUrl = string.IsNullOrEmpty(g.CroppedStoragePath ?? g.StoragePath)
                        ? null
                        : _storage.PublicUrlFor(g.CroppedStoragePath ?? g.StoragePath!),
                    RawUrl = string.IsNullOrEmpty(g.StoragePath)
                        ? null
                        : _storage.PublicUrlFor(g.StoragePath!)
                })
                .ToList(),
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        };
    }
}
