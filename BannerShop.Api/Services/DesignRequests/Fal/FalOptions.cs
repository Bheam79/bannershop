namespace BannerShop.Api.Services.DesignRequests.Fal;

/// <summary>
/// Non-secret fal.ai image-generation settings. The API key is stored in
/// <c>system_settings.fal_api_key</c> and is resolved for every request.
/// </summary>
public sealed class FalOptions
{
    public const string SectionName = "Fal";

    public string BaseUrl { get; set; } = "https://fal.run";
    public string ModelId { get; set; } = "fal-ai/flux-2-pro";
    public int TimeoutSeconds { get; set; } = 300;
}
