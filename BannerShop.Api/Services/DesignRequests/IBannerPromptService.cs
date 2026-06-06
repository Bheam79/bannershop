using BannerShop.Core.Enums;

namespace BannerShop.Api.Services.DesignRequests;

/// <summary>
/// Inputs to the prompt builder. Mirrors the customer-supplied <c>DesignRequest</c>
/// fields plus a hasPortrait hint so the prompt can opt into face-preservation copy.
/// </summary>
public record BannerPromptInput(
    BannerTemplateCategory Category,
    string Language,            // "nb" or "en"
    string PersonName,
    int? PersonAge,
    string TextContent,
    string ThemeDescription,
    string AspectRatio,         // "16:9" or "18:9"
    bool HasPortrait
);

/// <summary>
/// Builds an English prompt for the image generator from the customer's request.
/// Norwegian text content is quoted verbatim — only the surrounding instructions
/// are localised.
/// </summary>
public interface IBannerPromptService
{
    /// <summary>Build the prompt string for the configured image generator.</summary>
    string BuildPrompt(BannerPromptInput input);
}
