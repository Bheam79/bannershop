namespace BannerShop.Api.Services.DesignRequests;

/// <summary>
/// Upscales an image to a higher resolution. Kept as a thin abstraction so a Replicate /
/// Real-ESRGAN backend can be added later without changing pipeline code.
///
/// Per BANNERSH-18, v1 ships with <see cref="NoopUpscalingService"/> because
/// gpt-image-2 natively outputs 4K — no upscaling step is required.
/// </summary>
public interface IUpscalingService
{
    /// <summary>
    /// Upscale the file at <paramref name="inputAbsolutePath"/>. The return value is the
    /// absolute path of the upscaled file (may be the same path when noop).
    /// </summary>
    Task<string> UpscaleAsync(string inputAbsolutePath, int scale = 4, CancellationToken ct = default);
}
