namespace BannerShop.Api.Services.DesignRequests;

/// <summary>
/// Customer-facing upscaling is a noop; provider output is persisted at its native size.
/// Returns the input path unchanged. Replaced by a real provider (Replicate / Real-ESRGAN)
/// if a future task requires upscaling smaller source images.
/// </summary>
public sealed class NoopUpscalingService : IUpscalingService
{
    public Task<string> UpscaleAsync(string inputAbsolutePath, int scale = 4, CancellationToken ct = default)
        => Task.FromResult(inputAbsolutePath);
}
