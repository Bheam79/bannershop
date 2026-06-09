using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Core.Helpers;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Services.DesignRequests;

/// <summary>
/// Runs the AI pipeline for one <see cref="DesignRequest"/> generation attempt:
///   1. Create / activate a <see cref="BannerGeneration"/> row for this run.
///   2. Build prompt from template + customer inputs.
///   3. Generate the background image via <see cref="IAiImageService"/>.
///   4. Pass it through <see cref="IUpscalingService"/> (noop in v1 — see BANNERSH-18).
///   5. Center-crop to the customer's aspect ratio.
///   6. Persist the raw AI output and the cropped print file; update generation + request status.
///
/// Stateless; one instance per scope. Failures flip the generation to <c>Failed</c>
/// and the request to <c>Failed</c> with <see cref="DesignRequest.LastError"/> set.
/// </summary>
public sealed class AiGenerationPipeline
{
    private readonly BannerShopDbContext _db;
    private readonly IBannerPromptService _prompts;
    private readonly IPromptRefinementService _refiner;
    private readonly IAiImageService _ai;
    private readonly IUpscalingService _upscaler;
    private readonly IImageProcessingService _images;
    private readonly BannerFileStorage _storage;
    private readonly ILogger<AiGenerationPipeline> _log;

    public AiGenerationPipeline(
        BannerShopDbContext db,
        IBannerPromptService prompts,
        IPromptRefinementService refiner,
        IAiImageService ai,
        IUpscalingService upscaler,
        IImageProcessingService images,
        BannerFileStorage storage,
        ILogger<AiGenerationPipeline> log)
    {
        _db = db;
        _prompts = prompts;
        _refiner = refiner;
        _ai = ai;
        _upscaler = upscaler;
        _images = images;
        _storage = storage;
        _log = log;
    }

    public async Task RunAsync(int designRequestId, CancellationToken ct)
    {
        var request = await _db.DesignRequests
            .Include(r => r.BannerTemplate)
            .FirstOrDefaultAsync(r => r.Id == designRequestId, ct);
        if (request is null)
        {
            _log.LogWarning("Pipeline: DesignRequest {Id} not found.", designRequestId);
            return;
        }
        if (request.Mode != DesignRequestMode.Ai)
        {
            _log.LogWarning("Pipeline: DesignRequest {Id} is not AI mode (Mode={Mode}); skipping.", designRequestId, request.Mode);
            return;
        }
        // Guard against terminal states where the customer is already done.
        // AwaitingApproval is NOT in this list — it is reset to InProgress by the /regenerate endpoint.
        if (request.Status is DesignRequestStatus.Approved
                            or DesignRequestStatus.Final
                            or DesignRequestStatus.Cancelled)
        {
            _log.LogDebug("Pipeline: DesignRequest {Id} in terminal status {Status}; skipping.", designRequestId, request.Status);
            return;
        }

        // ── Step 1: acquire / create BannerGeneration row ─────────────────────
        // Look for a Pending row created by the /regenerate endpoint.
        // If none found (initial generation or pipeline retry), create one now.
        var generation = await _db.BannerGenerations
            .Where(g => g.DesignRequestId == designRequestId && g.Status == BannerGenerationStatus.Pending)
            .OrderByDescending(g => g.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (generation is null)
        {
            generation = new BannerGeneration
            {
                DesignRequestId = designRequestId,
                Status = BannerGenerationStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
            _db.BannerGenerations.Add(generation);
            await _db.SaveChangesAsync(ct);
        }

        // Deactivate all previous active generations for this request.
        var previousActive = await _db.BannerGenerations
            .Where(g => g.DesignRequestId == designRequestId && g.IsActive && g.Id != generation.Id)
            .ToListAsync(ct);
        foreach (var prev in previousActive)
            prev.IsActive = false;

        // Mark the current generation as active + processing.
        generation.IsActive = true;
        generation.Status = BannerGenerationStatus.Processing;
        await _db.SaveChangesAsync(ct);

        try
        {
            // 2. Build prompt
            var referenceAbs = string.IsNullOrEmpty(request.UploadedPhotoPath)
                ? null
                : _storage.AbsolutePathFor(request.UploadedPhotoPath);
            if (referenceAbs is not null && !File.Exists(referenceAbs))
            {
                _log.LogWarning("Pipeline: reference image {Path} missing — proceeding without portrait.", referenceAbs);
                referenceAbs = null;
            }

            var basePrompt = _prompts.BuildPrompt(new BannerPromptInput(
                Category: request.BannerTemplate.Category,
                Language: request.Language,
                PersonName: request.PersonName,
                PersonAge: request.PersonAge,
                TextContent: request.TextContent,
                ThemeDescription: request.ThemeDescription,
                AspectRatio: request.AspectRatio,
                HasPortrait: referenceAbs is not null));

            // BANNERSH-155: surface the deterministic base prompt in journalctl
            // so operators can verify the celebrant name (and other inputs) made
            // it into the overlay instruction before the LLM refinement step.
            _log.LogInformation(
                "Pipeline: DesignRequest {Id} base prompt: {BasePrompt}",
                designRequestId, basePrompt);

            // 2b. Refine the prompt via LLM (BANNERSH-61). Failures fall back
            // to the deterministic base prompt — see IPromptRefinementService.
            var prompt = await _refiner.RefineAsync(new PromptRefinementInput(
                Category: request.BannerTemplate.Category,
                Language: request.Language,
                PersonName: request.PersonName,
                PersonAge: request.PersonAge,
                TextContent: request.TextContent,
                ThemeDescription: request.ThemeDescription,
                AspectRatio: request.AspectRatio,
                HasPortrait: referenceAbs is not null,
                BasePrompt: basePrompt), ct);
            if (string.IsNullOrWhiteSpace(prompt))
                prompt = basePrompt;

            // BANNERSH-155: log the FINAL prompt actually sent to the image model
            // (post-refinement, or the base prompt if refinement was a no-op /
            // fell back). This is what gpt-image-2 sees.
            _log.LogInformation(
                "Pipeline: DesignRequest {Id} final prompt: {FinalPrompt}",
                designRequestId, prompt);

            // 3. Generate
            // BANNERSH-98: explicitly log the IAiImageService implementation type
            // so operators can confirm at a glance whether the real OpenAI-backed
            // service or a fallback (Mock/Placeholder) is wired in.
            _log.LogInformation(
                "Pipeline: generating image for DesignRequest {Id} (generation {GenId}) using {AiServiceType}",
                designRequestId, generation.Id, _ai.GetType().FullName);
            var generated = await _ai.GenerateAsync(
                new AiImageRequest(prompt, request.AspectRatio, referenceAbs), ct);

            // 4. Upscale (noop in v1)
            var upscaledAbs = await _upscaler.UpscaleAsync(generated.AbsolutePath, scale: 4, ct);

            // 5. Persist raw AI output to permanent storage.
            // UserId is null for anonymous (BANNERSH-67) — use bucket "0" in that case.
            var storageUserId = request.UserId ?? 0;
            var userDir = _storage.EnsureUserDirectory(storageUserId);
            var resultFileName = $"design_{request.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}.png";
            var resultAbs = Path.Combine(userDir, resultFileName);
            File.Copy(upscaledAbs, resultAbs, overwrite: true);
            var resultRelative = BannerFileStorage.RelativePathFor(storageUserId, resultFileName);

            // Best-effort cleanup of temp files.
            TryDelete(generated.AbsolutePath);
            if (!string.Equals(upscaledAbs, generated.AbsolutePath, StringComparison.Ordinal))
                TryDelete(upscaledAbs);

            // 6. Crop to the customer's aspect ratio (only needed for 18:9).
            string finalRelative = resultRelative;
            string finalAbs = resultAbs;
            if (request.AspectRatio == "18:9")
            {
                var croppedFileName = $"design_{request.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}_crop.png";
                var croppedAbs = Path.Combine(userDir, croppedFileName);
                await _images.CenterCropAsync(resultAbs, croppedAbs, ratioWidth: 2, ratioHeight: 1, ct);
                finalRelative = BannerFileStorage.RelativePathFor(storageUserId, croppedFileName);
                finalAbs = croppedAbs;
            }

            // 6b. Generate a low-res JPEG preview (max 640 px on the longer side — BANNERSH-91).
            // This is what customers see; the full-res finalRelative is reserved for printing.
            const int PreviewMaxPx = 640;
            const int PreviewQuality = 72;
            var previewFileName = $"design_{request.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}_preview.jpg";
            var previewAbs = Path.Combine(userDir, previewFileName);
            await _images.GeneratePreviewAsync(finalAbs, previewAbs,
                rotationDegrees: 0, maxWidth: PreviewMaxPx, quality: PreviewQuality, ct);
            var previewRelative = BannerFileStorage.RelativePathFor(storageUserId, previewFileName);

            // 7. Persist results: update BannerGeneration and DesignRequest.
            generation.StoragePath = resultRelative;
            generation.CroppedStoragePath = finalRelative;
            generation.Status = BannerGenerationStatus.Completed;
            generation.CompletedAt = DateTime.UtcNow;

            // Keep backward-compat fields on DesignRequest populated for existing callers.
            request.AiResultStoragePath = resultRelative;
            request.FinalCroppedStoragePath = finalRelative;
            request.AiPreviewPath = previewRelative;
            request.CurrentGenerationId = generation.Id;
            request.Status = DesignRequestStatus.AwaitingApproval;
            request.LastError = null;
            request.UpdatedAt = DateTime.UtcNow;

            // Advance the linked Order state: Paid → CustomerApproval (BANNERSH-109).
            if (request.OrderId.HasValue)
            {
                var order = await _db.Orders.FindAsync(new object?[] { request.OrderId.Value }, ct);
                if (order is not null
                    && OrderStateHelper.IsValidTransition(order.OrderType, order.OrderState, OrderState.CustomerApproval))
                {
                    order.OrderState = OrderState.CustomerApproval;
                    order.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _db.SaveChangesAsync(ct);

            _log.LogInformation("Pipeline: DesignRequest {Id} -> AwaitingApproval (gen={GenId}, path={Path})",
                request.Id, generation.Id, finalRelative);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Pipeline: failure for DesignRequest {Id} (generation {GenId})", designRequestId, generation.Id);
            generation.Status = BannerGenerationStatus.Failed;
            generation.ErrorMessage = ex.Message;
            generation.CompletedAt = DateTime.UtcNow;

            request.Status = DesignRequestStatus.Failed;
            request.LastError = ex.Message;
            request.UpdatedAt = DateTime.UtcNow;
            try { await _db.SaveChangesAsync(ct); }
            catch (Exception saveEx) { _log.LogError(saveEx, "Pipeline: failed to persist Failed status for {Id}", designRequestId); }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }
}
