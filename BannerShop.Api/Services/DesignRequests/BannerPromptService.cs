using System.Globalization;
using System.Text;
using BannerShop.Core.Enums;

namespace BannerShop.Api.Services.DesignRequests;

/// <summary>
/// Builds English prompts from <see cref="BannerPromptInput"/>s, one base sentence
/// per <see cref="BannerTemplateCategory"/>. The customer's exact <c>TextContent</c>
/// is quoted verbatim so the model is told to render it as overlay text.
/// </summary>
public sealed class BannerPromptService : IBannerPromptService
{
    public string BuildPrompt(BannerPromptInput input)
    {
        var sb = new StringBuilder();

        sb.Append(CategoryOpener(input.Category));

        var theme = (input.ThemeDescription ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(theme))
            sb.Append(", in ").Append(theme).Append(" style");
        sb.Append('.');

        if (input.HasPortrait)
        {
            sb.Append(" Feature a prominent portrait of ").Append(SafeName(input.PersonName));
            if (input.PersonAge is int age and > 0 and < 130)
                sb.Append(", ").Append(age.ToString(CultureInfo.InvariantCulture)).Append(" years old");
            sb.Append(" — preserve the face from the reference image.");
        }
        else if (!string.IsNullOrWhiteSpace(input.PersonName))
        {
            sb.Append(" The banner celebrates ").Append(SafeName(input.PersonName));
            if (input.PersonAge is int age2 and > 0 and < 130)
                sb.Append(", aged ").Append(age2.ToString(CultureInfo.InvariantCulture));
            sb.Append('.');
        }

        var text = (input.TextContent ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(text))
        {
            var langName = LanguageDisplayName(input.Language);
            sb.Append(" Overlay this text in large, readable ")
              .Append(langName)
              .Append(" typography: \"")
              .Append(text.Replace("\"", "\\\""))
              .Append("\".");
        }

        sb.Append(" Photorealistic, print-quality, ")
          .Append(AspectRatioPhrase(input.AspectRatio))
          .Append(", sharp, no watermarks, no logos.");

        return sb.ToString();
    }

    private static string CategoryOpener(BannerTemplateCategory category) => category switch
    {
        BannerTemplateCategory.Birthday     => "A vibrant, cheerful birthday-party banner with festive decorations, balloons and confetti",
        BannerTemplateCategory.Confirmation => "An elegant Norwegian confirmation banner with classic, dignified decorations and a celebratory tone",
        BannerTemplateCategory.Wedding      => "A romantic, refined wedding banner with floral decorations, soft colours and an elegant atmosphere",
        BannerTemplateCategory.Anniversary  => "A celebratory anniversary banner with warm, timeless decorations and elegant typography",
        BannerTemplateCategory.Christmas    => "A festive Christmas-party banner with snowflakes, fir branches, candles and warm holiday colours",
        BannerTemplateCategory.NewYear      => "A festive New Year's Eve party banner with fireworks, sparkles and gold/silver accents",
        BannerTemplateCategory.Other        => "A celebratory event banner with stylish, festive decorations",
        _                                   => "A celebratory event banner with festive decorations"
    };

    private static string LanguageDisplayName(string? language) =>
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "English" : "Norwegian";

    private static string AspectRatioPhrase(string? aspectRatio) => (aspectRatio ?? string.Empty).Trim() switch
    {
        "18:9" => "ultra-wide 2:1 landscape",
        _       => "16:9 landscape"
    };

    private static string SafeName(string? raw)
    {
        var name = (raw ?? string.Empty).Trim();
        return string.IsNullOrEmpty(name) ? "the celebrant" : name;
    }
}
