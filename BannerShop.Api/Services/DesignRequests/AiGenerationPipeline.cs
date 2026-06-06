using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Services.DesignRequests;

/// <summary>
/// Runs the AI pipeline for one <see cref="DesignRequest"/>:
///   1. Build prompt from template + customer inputs.
///   2. Generate the background image via <see cref="IAiImageService"/>.
///   3. Pass it through <see cref="IUpscalingService"/> (noop in v1 — see BANNERSH-18).
///   4. Center-crop to the customer's aspect ratio.
///   5. Persist the raw AI output and the cropped print file, update status.
///
/// Stateless; one instance per scope. Failures flip the request to <c>Failed</c>
/// status with <see cref="DesignRequest.LastError"/> set.
/// </summary>
public sealed class AiGenerationPipeline
{
    private readonly BannerShopDbContext _db;
    private readonly IBannerPromptService _prompts;
    private readonly IAiImageService _ai;
    private readonly IUpscalingService _upscaler;
    private readonly IImageProcessingService _images;
    private readonly BannerFileStorage _storage;
    private readonly ILogger<AiGenerationPipeline> _log;

    public AiGenerationPipeline(
        BannerShopDbContext db,
        IBannerPromptService prompts,
        IAiImageService ai,
        IUpscalingService upscaler,
        IImageProcessingService images,
        BannerFileStorage storage,
        ILogger<AiGenerationPipeline> log)
    {
        _db = db;
        _prompts = prompts;
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
        if (request.Status is DesignRequestStatus.AwaitingApproval
                            or DesignRequestStatus.Approved
                            or DesignRequestStatus.Final)
        {
            _log.LogDebug("Pipeline: DesignRequest {Id} already in terminal-ish status {Status}; skipping.", designRequestId, request.Status);
            return;
        }

        try
        {
            // 1. Build prompt
            var referenceAbs = string.IsNullOrEmpty(request.UploadedPhotoPath)
                ? null
                : _storage.AbsolutePathFor(request.UploadedPhotoPath);
            if (referenceAbs is not null && !File.Exists(referenceAbs))
            {
                _log.LogWarning("Pipeline: reference image {Path} missing — proceeding without portrait.", referenceAbs);
                referenceAbs = null;
            }

            var prompt = _prompts.BuildPrompt(new BannerPromptInput(
                Category: request.BannerTemplate.Category,
                Language: request.Language,
                PersonName: request.PersonName,
                PersonAge: request.PersonAge,
                TextContent: request.TextContent,
                ThemeDescription: request.ThemeDescription,
                AspectRatio: request.AspectRatio,
                HasPortrait: referenceAbs is not null));

            // 2. Generate
            _log.LogInformation("Pipeline: generating image for DesignRequest {Id}", designRequestId);
            var generated = await _ai.GenerateAsync(
                new AiImageRequest(prompt, request.AspectRatio, referenceAbs), ct);

            // 3. Upscale (noop in v1)
            var upscaledAbs = await _upscaler.UpscaleAsync(generated.AbsolutePath, scale: 4, ct);

            // 4. Persist raw AI output to permanent storage.
            var userDir = _storage.EnsureUserDirectory(request.UserId);
            var resultFileName = $"design_{request.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}.png";
            var resultAbs = Path.Combine(userDir, resultFileName);
            File.Copy(upscaledAbs, resultAbs, overwrite: true);
            var resultRelative = BannerFileStorage.RelativePathFor(request.UserId, resultFileName);

            // Best-effort cleanup of temp files.
            TryDelete(generated.AbsolutePath);
            if (!string.Equals(upscaledAbs, generated.AbsolutePath, StringComparison.Ordinal))
                TryDelete(upscaledAbs);

            // 5. Crop to the customer's aspect ratio (only needed for 18:9).
            string finalRelative = resultRelative;
            if (request.AspectRatio == "18:9")
            {
                var croppedFileName = $"design_{request.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}_crop.png";
                var croppedAbs = Path.Combine(userDir, croppedFileName);
                await _images.CenterCropAsync(resultAbs, croppedAbs, ratioWidth: 2, ratioHeight: 1, ct);
                finalRelative = BannerFileStorage.RelativePathFor(request.UserId, croppedFileName);
            }

            request.AiResultStoragePath = resultRelative;
            request.FinalCroppedStoragePath = finalRelative;
            request.Status = DesignRequestStatus.AwaitingApproval;
            request.LastError = null;
            request.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            _log.LogInformation("Pipeline: DesignRequest {Id} -> AwaitingApproval ({Path})", request.Id, finalRelative);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Pipeline: failure for DesignRequest {Id}", designRequestId);
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
