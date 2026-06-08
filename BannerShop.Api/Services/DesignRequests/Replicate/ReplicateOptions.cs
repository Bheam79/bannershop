namespace BannerShop.Api.Services.DesignRequests.Replicate;

/// <summary>
/// Configuration bound from the "Replicate" appsettings section.
///
/// Currently consumed by <see cref="RealEsrganUpscalingService"/> to call the
/// nightmareai/real-esrgan model for 4x upscaling of generated AI images.
///
/// Per BANNERSH-57: the Real-ESRGAN upscaler is an admin/order-backend step
/// (not part of the customer-facing AI preview pipeline) — production prints
/// benefit from a 4x bump on top of the model's native output.
/// </summary>
public class ReplicateOptions
{
    public const string SectionName = "Replicate";

    /// <summary>Replicate API token (r8_...). When empty, the service is not registered.</summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>
    /// Pinned model version hash for nightmareai/real-esrgan. Replicate requires
    /// a specific version hash for predictions; default is the current latest at
    /// time of writing. Override in appsettings to upgrade.
    /// </summary>
    public string RealEsrganModelVersion { get; set; }
        = "f121d640bd286e1fdc67f9799164c1d5be36ff74576ee11c803ae5b665dd46aa";

    /// <summary>Replicate REST base URL.</summary>
    public string BaseUrl { get; set; } = "https://api.replicate.com";

    /// <summary>Per-HTTP-request timeout (used for the initial create + each poll).</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>How long to wait between polls of the prediction status endpoint.</summary>
    public int PollIntervalMs { get; set; } = 2000;

    /// <summary>Maximum overall time we will wait for a prediction to complete.</summary>
    public int MaxPollSeconds { get; set; } = 600;
}
