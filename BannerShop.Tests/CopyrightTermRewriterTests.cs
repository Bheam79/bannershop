using BannerShop.Api.Services.DesignRequests;
using BannerShop.Core.Enums;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests;

public class CopyrightTermRewriterTests
{
    // ── Null / empty guard ──────────────────────────────────────────────────────

    [Fact]
    public void Returns_empty_string_for_null_input()
    {
        CopyrightTermRewriter.Rewrite(null).Should().BeEmpty();
    }

    [Fact]
    public void Returns_empty_string_for_whitespace_input()
    {
        CopyrightTermRewriter.Rewrite("   ").Should().BeEmpty();
    }

    [Fact]
    public void Returns_clean_text_unchanged()
    {
        const string clean = "rosa og gull, blomster, norsk flagg";
        CopyrightTermRewriter.Rewrite(clean).Should().Be(clean);
    }

    // ── Superheroes ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Spiderman theme")]
    [InlineData("Spider-Man tema")]
    [InlineData("Spider Man birthday")]
    public void Rewrites_spider_man_variants(string input)
    {
        var result = CopyrightTermRewriter.Rewrite(input);
        result.Should().NotContainEquivalentOf("spiderman");
        result.Should().NotContainEquivalentOf("spider-man");
        result.Should().NotContainEquivalentOf("spider man");
        result.Should().ContainEquivalentOf("spider-themed superhero");
    }

    [Theory]
    [InlineData("Batman theme")]
    [InlineData("batman-inspirert")]
    public void Rewrites_batman(string input)
    {
        var result = CopyrightTermRewriter.Rewrite(input);
        result.Should().NotContainEquivalentOf("batman");
        result.Should().ContainEquivalentOf("bat symbol");
    }

    [Theory]
    [InlineData("Superman birthday party")]
    [InlineData("superman tema")]
    public void Rewrites_superman(string input)
    {
        var result = CopyrightTermRewriter.Rewrite(input);
        result.Should().NotContainEquivalentOf("superman");
        result.Should().ContainEquivalentOf("caped superhero");
    }

    [Fact]
    public void Rewrites_iron_man()
    {
        var result = CopyrightTermRewriter.Rewrite("Iron Man theme, red");
        result.Should().NotContainEquivalentOf("iron man");
        result.Should().ContainEquivalentOf("red-and-gold powered suit");
    }

    [Fact]
    public void Rewrites_avengers()
    {
        var result = CopyrightTermRewriter.Rewrite("Avengers assemble theme");
        result.Should().NotContainEquivalentOf("avengers");
        result.Should().ContainEquivalentOf("team of superheroes");
    }

    // ── Star Wars ───────────────────────────────────────────────────────────────

    [Fact]
    public void Rewrites_star_wars()
    {
        var result = CopyrightTermRewriter.Rewrite("Star Wars galaxy theme");
        result.Should().NotContainEquivalentOf("star wars");
        result.Should().ContainEquivalentOf("starships");
    }

    [Fact]
    public void Rewrites_darth_vader()
    {
        var result = CopyrightTermRewriter.Rewrite("Darth Vader inspired, dark");
        result.Should().NotContainEquivalentOf("darth vader");
        result.Should().ContainEquivalentOf("dark lord");
    }

    [Theory]
    [InlineData("Baby Yoda theme")]
    [InlineData("grogu themed")]
    public void Rewrites_baby_yoda_and_grogu(string input)
    {
        var result = CopyrightTermRewriter.Rewrite(input);
        result.Should().NotContainEquivalentOf("yoda");
        result.Should().NotContainEquivalentOf("grogu");
        result.Should().ContainEquivalentOf("green-eared alien baby");
    }

    // ── Disney characters ───────────────────────────────────────────────────────

    [Fact]
    public void Rewrites_frozen()
    {
        var result = CopyrightTermRewriter.Rewrite("Frozen tema, blå og hvit");
        result.Should().NotContainEquivalentOf("frozen");
        result.Should().ContainEquivalentOf("ice palace");
    }

    [Fact]
    public void Rewrites_elsa()
    {
        var result = CopyrightTermRewriter.Rewrite("Elsa-inspirert, is og snø");
        result.Should().NotContainEquivalentOf("elsa");
        result.Should().ContainEquivalentOf("ice queen");
    }

    [Fact]
    public void Rewrites_mickey_mouse()
    {
        var result = CopyrightTermRewriter.Rewrite("Mickey Mouse clubhouse");
        result.Should().NotContainEquivalentOf("mickey mouse");
        result.Should().ContainEquivalentOf("cartoon mouse");
    }

    [Fact]
    public void Rewrites_lion_king()
    {
        var result = CopyrightTermRewriter.Rewrite("Lion King savanna theme");
        result.Should().NotContainEquivalentOf("lion king");
        result.Should().ContainEquivalentOf("lion cub");
    }

    [Fact]
    public void Rewrites_moana()
    {
        var result = CopyrightTermRewriter.Rewrite("Moana-inspirert, hav og strand");
        result.Should().NotContainEquivalentOf("moana");
        result.Should().ContainEquivalentOf("Polynesian ocean adventurer");
    }

    // ── Pixar ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Rewrites_toy_story()
    {
        var result = CopyrightTermRewriter.Rewrite("Toy Story western cowboy");
        result.Should().NotContainEquivalentOf("toy story");
        result.Should().ContainEquivalentOf("cowboy doll");
    }

    [Fact]
    public void Rewrites_finding_nemo()
    {
        var result = CopyrightTermRewriter.Rewrite("Finding Nemo ocean theme");
        result.Should().NotContainEquivalentOf("finding nemo");
        result.Should().ContainEquivalentOf("clownfish");
    }

    // ── Nintendo / Pokémon ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("Super Mario bros birthday")]
    [InlineData("mario tema")]
    public void Rewrites_mario_as_plumber(string input)
    {
        var result = CopyrightTermRewriter.Rewrite(input);
        result.Should().NotContainEquivalentOf("mario");
        result.Should().ContainEquivalentOf("plumber");
    }

    [Fact]
    public void Rewrites_mario_kart_as_racing_adventure()
    {
        var result = CopyrightTermRewriter.Rewrite("Mario kart race theme");
        result.Should().NotContainEquivalentOf("mario");
        result.Should().ContainEquivalentOf("racing adventure");
    }

    [Fact]
    public void Rewrites_pikachu()
    {
        var result = CopyrightTermRewriter.Rewrite("Pikachu Pokemon party");
        result.Should().NotContainEquivalentOf("pikachu");
        result.Should().ContainEquivalentOf("electric mouse");
    }

    [Theory]
    [InlineData("Pokemon adventure")]
    [InlineData("Pokémon world")]
    public void Rewrites_pokemon_variants(string input)
    {
        var result = CopyrightTermRewriter.Rewrite(input);
        result.Should().NotContainEquivalentOf("pokemon");
        result.Should().ContainEquivalentOf("pocket-monster");
    }

    // ── Video games ─────────────────────────────────────────────────────────────

    [Fact]
    public void Rewrites_minecraft()
    {
        var result = CopyrightTermRewriter.Rewrite("Minecraft grønn og brun");
        result.Should().NotContainEquivalentOf("minecraft");
        result.Should().ContainEquivalentOf("blocky pixelated world");
    }

    [Fact]
    public void Rewrites_fortnite()
    {
        var result = CopyrightTermRewriter.Rewrite("Fortnite battle royale");
        result.Should().NotContainEquivalentOf("fortnite");
        result.Should().ContainEquivalentOf("battle arena");
    }

    [Fact]
    public void Rewrites_roblox()
    {
        var result = CopyrightTermRewriter.Rewrite("Roblox game world");
        result.Should().NotContainEquivalentOf("roblox");
        result.Should().ContainEquivalentOf("blocky game world");
    }

    // ── Children's TV ───────────────────────────────────────────────────────────

    [Fact]
    public void Rewrites_peppa_pig()
    {
        var result = CopyrightTermRewriter.Rewrite("Peppa Pig tema, rosa");
        result.Should().NotContainEquivalentOf("peppa pig");
        result.Should().ContainEquivalentOf("pink cartoon pig");
    }

    [Fact]
    public void Rewrites_paw_patrol()
    {
        var result = CopyrightTermRewriter.Rewrite("PAW Patrol adventure");
        result.Should().NotContainEquivalentOf("paw patrol");
        result.Should().ContainEquivalentOf("rescue pups");
    }

    [Fact]
    public void Rewrites_bluey()
    {
        var result = CopyrightTermRewriter.Rewrite("Bluey og Bingo theme");
        result.Should().NotContainEquivalentOf("bluey");
        result.Should().ContainEquivalentOf("blue cartoon dog");
    }

    // ── Anime ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Rewrites_naruto()
    {
        var result = CopyrightTermRewriter.Rewrite("Naruto ninja theme");
        result.Should().NotContainEquivalentOf("naruto");
        result.Should().ContainEquivalentOf("orange jumpsuit");
    }

    // ── Other franchises ────────────────────────────────────────────────────────

    [Fact]
    public void Rewrites_harry_potter()
    {
        var result = CopyrightTermRewriter.Rewrite("Harry Potter magic school");
        result.Should().NotContainEquivalentOf("harry potter");
        result.Should().ContainEquivalentOf("young wizard");
    }

    // ── Word-boundary safety ─────────────────────────────────────────────────────

    [Fact]
    public void Does_not_rewrite_batman_inside_a_longer_token()
    {
        // "xbatmanx" has no word boundary so the rewriter must leave it untouched.
        const string input = "xbatmanx fest";
        var result = CopyrightTermRewriter.Rewrite(input);
        result.Should().ContainEquivalentOf("xbatmanx");
    }

    [Fact]
    public void Is_case_insensitive()
    {
        var lower = CopyrightTermRewriter.Rewrite("spiderman tema");
        var upper = CopyrightTermRewriter.Rewrite("SPIDERMAN TEMA");
        var mixed = CopyrightTermRewriter.Rewrite("SpiderMan Tema");

        lower.Should().NotContainEquivalentOf("spiderman");
        upper.Should().NotContainEquivalentOf("spiderman");
        mixed.Should().NotContainEquivalentOf("spiderman");
    }

    // ── Integration: BannerPromptService rewrites theme before embedding ─────────

    [Fact]
    public void BannerPromptService_does_not_embed_copyrighted_theme_in_prompt()
    {
        var svc = new BannerPromptService();
        var prompt = svc.BuildPrompt(new BannerPromptInput(
            Category: BannerTemplateCategory.Birthday,
            Language: "nb",
            PersonName: "Oliver",
            PersonAge: 7,
            TextContent: "Gratulerer med 7!",
            ThemeDescription: "Spiderman theme, rød og blå",
            AspectRatio: "16:9",
            HasPortrait: false));

        // The rewriter must rewrite the theme; the resulting prompt must NOT
        // describe the banner *style* as "Spiderman" — instead it should
        // describe a "spider-themed superhero" style. The safety instruction
        // at the end of the prompt (BANNERSH-215) intentionally names Spider-Man
        // as an example of what the image generator must avoid, so we only
        // check the theme/style clause here.
        var themeClauseEnd = prompt.IndexOf(" Photorealistic", StringComparison.OrdinalIgnoreCase);
        themeClauseEnd.Should().BeGreaterThan(0);
        var bodyBeforeInstruction = prompt[..themeClauseEnd];
        bodyBeforeInstruction.Should().NotContainEquivalentOf("spiderman");
        bodyBeforeInstruction.Should().NotContainEquivalentOf("spider-man");
        prompt.Should().ContainEquivalentOf("spider-themed superhero");
    }

    [Fact]
    public void BannerPromptService_does_not_embed_minecraft_in_prompt()
    {
        var svc = new BannerPromptService();
        var prompt = svc.BuildPrompt(new BannerPromptInput(
            Category: BannerTemplateCategory.Birthday,
            Language: "nb",
            PersonName: "Noah",
            PersonAge: 8,
            TextContent: "Gratulerer med 8!",
            ThemeDescription: "Minecraft, grønn og brun",
            AspectRatio: "16:9",
            HasPortrait: false));

        prompt.Should().NotContainEquivalentOf("minecraft");
        prompt.Should().ContainEquivalentOf("blocky pixelated world");
    }
}
