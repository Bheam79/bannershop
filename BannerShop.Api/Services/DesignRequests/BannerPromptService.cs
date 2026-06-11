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

        // Rewrite any trademarked / copyrighted terms in the customer's theme
        // description so the downstream image model never receives protected IP.
        var theme = CopyrightTermRewriter.Rewrite(
            (input.ThemeDescription ?? string.Empty).Trim());
        if (!string.IsNullOrEmpty(theme))
            sb.Append(", in ").Append(theme).Append(" style");
        sb.Append('.');

        if (input.HasPortrait)
        {
            sb.Append(" Feature a prominent portrait of ").Append(SafeName(input.PersonName));
            if (input.PersonAge is int age and > 0 and < 130)
                sb.Append(", ").Append(age.ToString(CultureInfo.InvariantCulture)).Append(" years old");
            var pos = input.PortraitPosition is { Length: > 0 } p ? p : "naturally within the scene";
            sb.Append(", positioned ").Append(pos);
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
            var name     = (input.PersonName ?? string.Empty).Trim();

            if (!string.IsNullOrEmpty(name) && input.Category.IsPersonCentred())
            {
                // For person-centred celebrations the celebrant's name must appear as
                // rendered text on the banner, not just as background context.
                sb.Append(" Overlay these text elements in large, readable ")
                  .Append(langName)
                  .Append(" typography, well within the safe zone (at least 10% from every edge so nothing is clipped by print finishing or aspect-ratio crop): the name \"")
                  .Append(name.Replace("\"", "\\\""))
                  .Append("\" prominently, and below it the message \"")
                  .Append(text.Replace("\"", "\\\""))
                  .Append("\".");
            }
            else
            {
                sb.Append(" Overlay this text in large, readable ")
                  .Append(langName)
                  .Append(" typography, well within the safe zone (at least 10% from every edge so nothing is clipped): \"")
                  .Append(text.Replace("\"", "\\\""))
                  .Append("\".");
            }
        }

        sb.Append(" Photorealistic, print-quality, ")
          .Append(AspectRatioPhrase(input.AspectRatio))
          .Append(", sharp, no watermarks, no logos.");

        // BANNERSH-215: explicitly tell the image generator to avoid copyrighted
        // movie / TV / video-game characters. The instruction is localised to
        // match the customer's banner language so the model isn't getting mixed
        // English + Norwegian directives (the overlay text is already in the
        // customer's language, so a same-language safety instruction sits next
        // to it more cleanly than an English one).
        sb.Append(' ').Append(CopyrightAvoidanceInstruction(input.Language));

        return sb.ToString();
    }

    /// <summary>
    /// Short, language-matched instruction to the image generator that it must
    /// NOT include copyrighted movie / TV / video-game / book characters in the
    /// final banner image. Examples are listed so the model gets concrete cues,
    /// not just an abstract rule.
    /// </summary>
    private static string CopyrightAvoidanceInstruction(string? language)
    {
        var isNorwegian = !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
        return isNorwegian
            ? "Ikke bruk opphavsrettsbeskyttede figurer fra filmer, TV-serier, videospill, tegneserier eller bøker (f.eks. Spider-Man, Superman, Batman, Elsa, Pikachu, Mario, Harry Potter) — bruk kun generiske, beskrivende motiver."
            : "Do not include any copyrighted movie, TV, video-game, comic, or book characters (e.g. Spider-Man, Superman, Batman, Elsa, Pikachu, Mario, Harry Potter) — use only generic, descriptive motifs.";
    }

    private static string CategoryOpener(BannerTemplateCategory category) => category switch
    {
        BannerTemplateCategory.Birthday     => "A vibrant, cheerful birthday-party banner with festive decorations, balloons and confetti",
        BannerTemplateCategory.Confirmation => "An elegant Norwegian confirmation banner with classic, dignified decorations and a celebratory tone",
        BannerTemplateCategory.Baptism      => "A gentle, joyful Norwegian baptism (dåp) banner with soft pastel decorations, doves and delicate florals",
        BannerTemplateCategory.Wedding      => "A romantic, refined wedding banner with floral decorations, soft colours and an elegant atmosphere",
        BannerTemplateCategory.Anniversary  => "A celebratory anniversary banner with warm, timeless decorations and elegant typography",
        BannerTemplateCategory.Christmas    => "A festive Christmas-party banner with snowflakes, fir branches, candles and warm holiday colours",
        BannerTemplateCategory.NewYear      => "A festive New Year's Eve party banner with fireworks, sparkles and gold/silver accents",
        BannerTemplateCategory.Other        => "A celebratory event banner with stylish, festive decorations",
        _                                   => "A celebratory event banner with festive decorations"
    };

    private static string LanguageDisplayName(string? language) =>
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "English" : "Norwegian";

    /// <summary>
    /// Returns a short phrase describing the requested orientation that gets
    /// appended to the AI prompt. Recognises both legacy ratio labels
    /// ("16:9", "18:9") and the WxH dimensions strings the frontend sends
    /// since BANNERSH-170 (e.g. "267x150", "150x150", "90x180"). Without this
    /// the prompt always claimed "16:9 landscape" regardless of the customer's
    /// choice, so the model had no orientation hint — see BANNERSH-175.
    /// </summary>
    private static string AspectRatioPhrase(string? aspectRatio)
    {
        var raw = (aspectRatio ?? string.Empty).Trim();
        // Keep the legacy phrases verbatim so they aren't accidentally reworded
        // (and so the existing tests around "16:9 landscape" / "ultra-wide 2:1
        // landscape" still pass).
        if (raw == "16:9") return "16:9 landscape";
        if (raw == "18:9") return "ultra-wide 2:1 landscape";

        var ratio = ParseRatio(raw);
        if (ratio >= 2.5)  return "ultra-wide 3:1 panoramic landscape";
        if (ratio >= 1.75) return "16:9 landscape";
        if (ratio >= 1.25) return "wide 2:1 landscape";
        if (ratio >= 0.85) return "square 1:1";
        if (ratio >= 0.55) return "portrait 2:3";
        return "tall 1:2 portrait";
    }

    /// <summary>
    /// Parses an aspect-ratio string into a numeric W/H ratio. Accepts the
    /// "A:B" label form (e.g. "16:9") and the "WxH" dimensions form
    /// (e.g. "267x150"). Falls back to 16/9 when the input is empty or
    /// unparseable.
    /// </summary>
    private static double ParseRatio(string? aspectRatio)
    {
        if (string.IsNullOrWhiteSpace(aspectRatio)) return 16.0 / 9.0;
        var s = aspectRatio.Trim();

        var xIdx = s.IndexOfAny(['x', 'X']);
        if (xIdx > 0
            && int.TryParse(s[..xIdx], out var w)
            && int.TryParse(s[(xIdx + 1)..], out var h)
            && w > 0 && h > 0)
            return (double)w / h;

        var colonIdx = s.IndexOf(':');
        if (colonIdx > 0
            && int.TryParse(s[..colonIdx], out var a)
            && int.TryParse(s[(colonIdx + 1)..], out var b)
            && a > 0 && b > 0)
            return (double)a / b;

        return 16.0 / 9.0;
    }

    private static string SafeName(string? raw)
    {
        var name = (raw ?? string.Empty).Trim();
        return string.IsNullOrEmpty(name) ? "the celebrant" : name;
    }
}
