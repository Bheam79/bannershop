namespace BannerShop.Api.Services.DesignRequests.OpenAi;

/// <summary>
/// Configuration bound from the "OpenAi" appsettings section.
/// See BANNERSH-18 §7 for the locked-in defaults.
///
/// BANNERSH-161: The API key is NO LONGER read from appsettings — it must be
/// entered via the admin settings panel (system_settings row 'openai_api_key').
/// Non-secret tuning knobs (model, quality, base URL, etc.) stay here.
/// </summary>
public class OpenAiOptions
{
    public const string SectionName = "OpenAi";

    public string ImageModel { get; set; } = "gpt-image-2";
    public string ImageQuality { get; set; } = "high";

    /// <summary>
    /// Chat-completions model used by <c>OpenAiPromptRefinementService</c>
    /// (BANNERSH-61) to expand terse user input into a richer image-generation
    /// prompt. Defaults to a small fast model so the refinement step adds
    /// minimal latency to the customer-facing pipeline.
    /// </summary>
    public string ChatModel { get; set; } = "gpt-4o-mini";

    public string OrgId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com";
    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// Timeout for the (much cheaper) chat-completions prompt-refinement call.
    /// Kept short — if the LLM is slow, the pipeline falls back to the
    /// deterministic base prompt rather than making customers wait.
    /// </summary>
    public int ChatTimeoutSeconds { get; set; } = 30;
}
