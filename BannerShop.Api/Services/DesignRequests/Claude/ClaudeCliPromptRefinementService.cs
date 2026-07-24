using System.Globalization;
using System.Text;
using BannerShop.Api.Services.SystemSettings;
using BannerShop.Core.Enums;

namespace BannerShop.Api.Services.DesignRequests.Claude;

/// <summary>
/// Uses Claude Code to turn the customer's banner details into a vivid,
/// production-ready FLUX.2 Pro prompt. Prompt copy and category-specific art
/// direction are database settings so administrators can tune them live.
/// </summary>
public sealed class ClaudeCliPromptRefinementService : IPromptRefinementService
{
    public const string TokenSetting = "claude_code_oauth_token";
    public const string MainPromptSetting = "claude_flux_system_prompt";

    public const string DefaultMainPrompt =
        """
        You are an expert advertising art director and prompt engineer. Turn the supplied customer details into one vivid, highly specific English image-generation prompt for FLUX.2 Pro. The output image IS the finished large-format print banner: never show a banner, sign, poster, print, frame, mockup, wall, room, hanging fabric, or banner-within-a-banner. Demand a premium designed graphic composition rather than a plain photo collage. Explicitly describe the background scene, rich colour palette, lighting, layered decorative framing, depth, energy, subject placement, and large legible typography whose colour, shading and effects suit the scene. When @image1 is available, place that exact person as a professionally retouched integrated cutout, preserve their recognizable identity, and describe a tasteful themed outfit transformation. Keep all important faces and text at least 10% inside every edge. Preserve every supplied text string exactly and request no extra words. Convert trademarked characters or brands into descriptive, original visual attributes without names or logos. Specify sharp, photorealistic, print-quality detail and the requested aspect ratio. Reply with the final FLUX prompt only: one paragraph, no preamble, markdown or quotation marks around the whole answer.
        """;

    private static readonly IReadOnlyDictionary<BannerTemplateCategory, CategoryPrompt> CategoryPrompts =
        new Dictionary<BannerTemplateCategory, CategoryPrompt>
        {
            [BannerTemplateCategory.Birthday] = new(
                "claude_flux_category_birthday",
                "Create a celebratory birthday banner with vivid, joyful imagery, playful party energy, premium decorations and an age-appropriate visual style."),
            [BannerTemplateCategory.Confirmation] = new(
                "claude_flux_category_confirmation",
                "Create an elegant Norwegian confirmation banner with dignified modern styling, refined celebratory details and a confident youthful atmosphere."),
            [BannerTemplateCategory.Wedding] = new(
                "claude_flux_category_wedding",
                "Create a formal, romantic and elegant wedding banner with luxurious floral or decorative styling and a timeless premium finish."),
            [BannerTemplateCategory.Anniversary] = new(
                "claude_flux_category_anniversary",
                "Create a warm, sophisticated anniversary banner celebrating shared history with elegant layered details and timeless visual richness."),
            [BannerTemplateCategory.Christmas] = new(
                "claude_flux_category_christmas",
                "Create a vivid premium Christmas banner with atmospheric seasonal light, rich festive depth and elegant holiday decorations."),
            [BannerTemplateCategory.NewYear] = new(
                "claude_flux_category_new_year",
                "Create a glamorous, energetic New Year banner with dramatic celebration lighting, sparkling depth and a premium midnight-party atmosphere."),
            [BannerTemplateCategory.Other] = new(
                "claude_flux_category_other",
                "Create a vivid premium event banner tailored closely to the supplied theme, with a strong visual concept and polished graphic composition."),
            [BannerTemplateCategory.Baptism] = new(
                "claude_flux_category_baptism",
                "Create a gentle, joyful and elegant Norwegian baptism banner with luminous soft colour, refined symbolic details and a premium celebratory finish.")
        };

    private readonly IClaudeCliRunner _runner;
    private readonly ISystemSettingsService _settings;
    private readonly ILogger<ClaudeCliPromptRefinementService> _log;

    public ClaudeCliPromptRefinementService(
        IClaudeCliRunner runner,
        ISystemSettingsService settings,
        ILogger<ClaudeCliPromptRefinementService> log)
    {
        _runner = runner;
        _settings = settings;
        _log = log;
    }

    public async Task<string> RefineAsync(PromptRefinementInput input, CancellationToken ct)
    {
        // The DB value is authoritative, but the environment fallback makes a
        // freshly deployed installation work before an admin has saved it.
        var token = await _settings.GetValueAsync(TokenSetting, ct)
            ?? Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            _log.LogWarning(
                "Claude prompt refinement skipped: set {Setting} in /admin/settings " +
                "or CLAUDE_CODE_OAUTH_TOKEN in the service environment.",
                TokenSetting);
            return input.BasePrompt;
        }

        var mainPrompt = await _settings.GetValueAsync(MainPromptSetting, ct)
            ?? DefaultMainPrompt;
        var category = CategoryPrompts.TryGetValue(input.Category, out var configured)
            ? configured
            : CategoryPrompts[BannerTemplateCategory.Other];
        var categoryPrompt = await _settings.GetValueAsync(category.SettingKey, ct)
            ?? category.DefaultValue;

        try
        {
            var refined = await _runner.RunAsync(
                mainPrompt,
                BuildUserPrompt(input, categoryPrompt),
                token,
                ct);
            if (string.IsNullOrWhiteSpace(refined))
            {
                _log.LogWarning("Claude CLI returned an empty prompt; using deterministic base prompt.");
                return input.BasePrompt;
            }

            refined = StripFences(refined);
            _log.LogDebug(
                "Prompt refined by Claude CLI: base={BaseLength} chars -> refined={RefinedLength} chars.",
                input.BasePrompt.Length,
                refined.Length);
            return refined;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Claude CLI prompt refinement failed; using deterministic base prompt.");
            return input.BasePrompt;
        }
    }

    public static string SettingKeyFor(BannerTemplateCategory category) =>
        CategoryPrompts.TryGetValue(category, out var value)
            ? value.SettingKey
            : CategoryPrompts[BannerTemplateCategory.Other].SettingKey;

    public static string DefaultCategoryPrompt(BannerTemplateCategory category) =>
        CategoryPrompts.TryGetValue(category, out var value)
            ? value.DefaultValue
            : CategoryPrompts[BannerTemplateCategory.Other].DefaultValue;

    private static string BuildUserPrompt(PromptRefinementInput input, string categoryPrompt)
    {
        var theme = string.IsNullOrWhiteSpace(input.ThemeDescription)
            ? "(not supplied — invent a fitting vivid art direction)"
            : input.ThemeDescription.Trim();
        var text = string.IsNullOrWhiteSpace(input.TextContent)
            ? "(none)"
            : input.TextContent.Trim();

        var prompt = new StringBuilder();
        prompt.AppendLine(categoryPrompt);
        prompt.Append("Banner type: ").AppendLine(input.Category.ToString());
        prompt.Append("Name: ").AppendLine(
            string.IsNullOrWhiteSpace(input.PersonName) ? "(not supplied)" : input.PersonName.Trim());
        prompt.Append("Age: ").AppendLine(
            input.PersonAge is > 0
                ? input.PersonAge.Value.ToString(CultureInfo.InvariantCulture)
                : "(not supplied / not relevant)");
        prompt.Append("Exact text on banner: ").AppendLine(text);
        prompt.Append("Text language: ").AppendLine(
            string.Equals(input.Language, "en", StringComparison.OrdinalIgnoreCase)
                ? "English"
                : "Norwegian Bokmål");
        prompt.Append("Theme / style input: ").AppendLine(theme);
        prompt.Append("Portrait reference: ").AppendLine(
            input.HasPortrait
                ? "Yes. The downstream FLUX edit request attaches it as @image1."
                : "No.");
        prompt.Append("Aspect ratio: ").AppendLine(input.AspectRatio);
        prompt.AppendLine("Deterministic draft for factual constraints (improve it substantially):");
        prompt.AppendLine(input.BasePrompt);
        prompt.AppendLine(
            "Return only the vivid FLUX.2 Pro prompt. Remember: the generated image itself is the banner, not a scene containing a banner.");
        return prompt.ToString();
    }

    private static string StripFences(string value)
    {
        var result = value.Trim();
        if (!result.StartsWith("```", StringComparison.Ordinal))
            return result;

        var firstNewline = result.IndexOf('\n');
        if (firstNewline >= 0)
            result = result[(firstNewline + 1)..];
        if (result.EndsWith("```", StringComparison.Ordinal))
            result = result[..^3];
        return result.Trim();
    }

    private sealed record CategoryPrompt(string SettingKey, string DefaultValue);
}
