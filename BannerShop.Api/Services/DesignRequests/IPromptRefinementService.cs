using BannerShop.Core.Enums;

namespace BannerShop.Api.Services.DesignRequests;

/// <summary>
/// Inputs to a prompt-refinement pass. Mirrors <see cref="BannerPromptInput"/>
/// plus the deterministic <see cref="BasePrompt"/> produced by
/// <see cref="IBannerPromptService"/>. The refiner is free to lean on either:
/// rewrite the base prompt entirely, or extend it. Implementations MUST be safe
/// to fall back to <see cref="BasePrompt"/> on any error.
/// </summary>
public sealed record PromptRefinementInput(
    BannerTemplateCategory Category,
    string Language,
    string PersonName,
    int? PersonAge,
    string TextContent,
    string ThemeDescription,
    string AspectRatio,
    bool HasPortrait,
    string BasePrompt);

/// <summary>
/// Optional LLM-backed prompt rewriter that turns terse customer input
/// (e.g. ThemeDescription = "minecraft") into a richer prompt for the image
/// generator. Added in BANNERSH-61 so birthday/konfirmasjon/dåp/bryllup
/// banners get a higher-quality refined prompt before hitting gpt-image-2.
///
/// Lives behind an interface so the LLM provider can be swapped (OpenAI today,
/// Azure / open-source tomorrow). The pipeline always treats a refinement
/// failure as non-fatal: if the refiner throws, we use the base prompt.
/// </summary>
public interface IPromptRefinementService
{
    /// <summary>
    /// Produce a refined image-generation prompt. The returned string SHOULD
    /// preserve the user's <c>TextContent</c> verbatim (the model is told to
    /// render it as overlay text on the banner) and SHOULD reference the
    /// uploaded portrait when <paramref name="input"/>.HasPortrait is true.
    ///
    /// Implementations MUST return a non-empty string. Use the supplied
    /// <see cref="PromptRefinementInput.BasePrompt"/> as the fallback.
    /// </summary>
    Task<string> RefineAsync(PromptRefinementInput input, CancellationToken ct);
}
