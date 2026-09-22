using BannerShop.Api.Services.DesignRequests.Claude;
using BannerShop.Api.Services.DesignRequests.Cli;
using BannerShop.Api.Services.SystemSettings;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;

namespace BannerShop.Api.Services.DesignRequests;

public sealed record BuiltInPrompt(string Id, string Group, string Title, string Description,
    string BuiltIn, string Effective, string? SettingKey = null);
public sealed record PromptAgentModel(string Agent, string Role, string Model, string VersionNote);
public sealed record BannerPromptCatalogResult(IReadOnlyList<PromptAgentModel> Models, IReadOnlyList<BuiltInPrompt> Prompts);

/// <summary>Read-only allowlist: never includes credentials, customer data or native CLI hidden prompts.</summary>
public static class BannerPromptCatalog
{
    public static async Task<BannerPromptCatalogResult> BuildAsync(
        ISystemSettingsService settings, ClaudeCliOptions claude, CancellationToken ct)
    {
        var prompts = new List<BuiltInPrompt>();
        void Add(string id, string group, string title, string description, string text) =>
            prompts.Add(new(id, group, title, description, text, text));
        async Task AddSetting(string key, string title, string builtIn) =>
            prompts.Add(new(key, "Claude", title,
                "Lagret innstilling overstyrer standarden. Opphavsrettsinstruksen legges også til hovedprompten.",
                builtIn, await settings.GetValueAsync(key, ct) ?? builtIn, key));

        await AddSetting(ClaudeCliPromptRefinementService.MainPromptSetting, "Hovedprompt",
            ClaudeCliPromptRefinementService.DefaultMainPrompt);
        foreach (var category in Enum.GetValues<BannerTemplateCategory>())
            await AddSetting(ClaudeCliPromptRefinementService.SettingKeyFor(category), category.ToString(),
                ClaudeCliPromptRefinementService.DefaultCategoryPrompt(category));

        Add("claude-input", "Claude", "Kundedata til promptforbedring (eksempel)",
            "Plassholdere erstattes med kundens valg. Uten portrett står det No.; språk er English eller Norwegian Bokmål.",
            ClaudeCliPromptRefinementService.BuildUserPrompt(new(BannerTemplateCategory.Birthday, "nb",
                "{personName}", 30, "{textContent}", "{themeDescription}", "{aspectRatio}", true, "{basePrompt}"),
                "{categoryPrompt}"));

        foreach (var provider in new[] { "codex", "grok" })
        {
            foreach (var portrait in new[] { false, true })
                Add($"{provider}-{portrait}", provider == "codex" ? "Codex" : "Grok",
                    portrait ? "Med portrett" : "Uten portrett",
                    "Hele app-instruksen til bildeagenten. {finalPrompt} er forbedret prompt + felles krav. " +
                    "Grok får også innledningen som systemprompt. Portrettet skaleres med luft rundt, uten forvrengning.",
                    ImageProviderPrompts.Build(provider, new("{finalPrompt}", "{aspectRatio}", null),
                        portrait ? "{portraitPath}" : null));
        }

        Add("copyright", "Felles krav", "Opphavsrett / originale alternativer",
            "Legges til Claude-systemprompten, etter promptforbedring og i begge bildeagenters instruks.",
            ImageCopyrightInstruction.Text);
        Add("constraints", "Felles krav", "Krav etter promptforbedring (eksempel)",
            "Identitetskravet brukes bare med portrett. Navn inkluderes for personlige feiringer; tom tekst utelates.",
            AiGenerationPipeline.AppendNonNegotiableConstraints("{refinedPrompt}", new DesignRequest
            {
                BannerTemplate = new BannerTemplate { Category = BannerTemplateCategory.Birthday },
                PersonName = "{personName}", TextContent = "{textContent}"
            }, true));

        var builder = new BannerPromptService();
        foreach (var category in Enum.GetValues<BannerTemplateCategory>())
        {
            var variants = new List<string>();
            foreach (var language in new[] { "nb", "en" })
                foreach (var portrait in new[] { false, true })
                    variants.Add($"[{language}; {(portrait ? "med portrett" : "uten portrett")}]\n" +
                        builder.BuildPrompt(new(category, language, "{personName}", 30, "{textContent}",
                            "{themeDescription}", "16:9", portrait, "{portraitPosition}")));
            Add($"base-{category}", "Grunnprompt", category.ToString(),
                "Eksempler fra den faktiske promptbyggeren (alder 30, format 16:9). Brukes før Claude, " +
                "eller direkte hvis forbedring feiler. Tomme kundefelt utelates; alder, plassering og format følger bestillingen.",
                string.Join("\n\n", variants));
        }

        return new([
            new("Claude", "Promptforbedring", claude.Model,
                "Konfigurert ClaudeCli:Model. Et alias som sonnet er ikke en låst modellversjon; CLI-responsen rapporterer ikke eksakt versjon."),
            new("Codex", "Bildegenerering", "CLI-/leverandørstandard (ikke låst)",
                "Appen sender ingen modell-ID. Eksakt agent- og bildemodellversjon rapporteres ikke av denne integrasjonen."),
            new("Grok", "Bildegenerering", "CLI-/leverandørstandard (ikke låst)",
                "Appen sender ingen modell-ID. Eksakt agent- og bildemodellversjon rapporteres ikke av denne integrasjonen.")
        ], prompts);
    }
}
