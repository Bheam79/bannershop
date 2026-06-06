namespace BannerShop.Api.Services.DesignRequests.OpenAi;

/// <summary>
/// Configuration bound from the "OpenAi" appsettings section.
/// See BANNERSH-18 §7 for the locked-in defaults.
/// </summary>
public class OpenAiOptions
{
    public const string SectionName = "OpenAi";

    public string ApiKey { get; set; } = string.Empty;
    public string ImageModel { get; set; } = "gpt-image-2";
    public string ImageQuality { get; set; } = "high";
    public string OrgId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com";
    public int TimeoutSeconds { get; set; } = 180;
}
