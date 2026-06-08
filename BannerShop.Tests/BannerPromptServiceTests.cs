using BannerShop.Api.Services.DesignRequests;
using BannerShop.Core.Enums;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests;

public class BannerPromptServiceTests
{
    private readonly BannerPromptService _sut = new();

    [Fact]
    public void Quotes_text_content_verbatim_in_norwegian_typography_for_nb_language()
    {
        var prompt = _sut.BuildPrompt(new BannerPromptInput(
            Category: BannerTemplateCategory.Birthday,
            Language: "nb",
            PersonName: "Ola",
            PersonAge: 40,
            TextContent: "Gratulerer med 40!",
            ThemeDescription: "tropisk, lilla",
            AspectRatio: "16:9",
            HasPortrait: false));

        prompt.Should().Contain("birthday-party");
        prompt.Should().Contain("\"Gratulerer med 40!\"");
        prompt.Should().Contain("Norwegian typography");
        prompt.Should().Contain("16:9 landscape");
    }

    [Fact]
    public void Includes_reference_image_clause_when_portrait_uploaded()
    {
        var prompt = _sut.BuildPrompt(new BannerPromptInput(
            Category: BannerTemplateCategory.Wedding,
            Language: "en",
            PersonName: "Alice",
            PersonAge: null,
            TextContent: "Just Married",
            ThemeDescription: "garden, white",
            AspectRatio: "18:9",
            HasPortrait: true));

        prompt.Should().Contain("wedding");
        prompt.Should().Contain("Alice");
        prompt.Should().Contain("preserve the face from the reference image");
        prompt.Should().Contain("English typography");
        prompt.Should().Contain("ultra-wide 2:1 landscape");
    }

    [Theory]
    [InlineData(BannerTemplateCategory.Birthday)]
    [InlineData(BannerTemplateCategory.Confirmation)]
    [InlineData(BannerTemplateCategory.Baptism)]
    [InlineData(BannerTemplateCategory.Wedding)]
    [InlineData(BannerTemplateCategory.Anniversary)]
    [InlineData(BannerTemplateCategory.Christmas)]
    [InlineData(BannerTemplateCategory.NewYear)]
    [InlineData(BannerTemplateCategory.Other)]
    public void Every_template_category_produces_a_non_empty_prompt(BannerTemplateCategory category)
    {
        var prompt = _sut.BuildPrompt(new BannerPromptInput(
            Category: category,
            Language: "nb",
            PersonName: "Test",
            PersonAge: 30,
            TextContent: "Test",
            ThemeDescription: "Test",
            AspectRatio: "16:9",
            HasPortrait: false));

        prompt.Should().NotBeNullOrWhiteSpace();
        prompt.Should().EndWith("no logos.");
    }

    [Fact]
    public void Omits_age_when_out_of_range()
    {
        var prompt = _sut.BuildPrompt(new BannerPromptInput(
            Category: BannerTemplateCategory.Birthday,
            Language: "nb",
            PersonName: "Kid",
            PersonAge: 0,
            TextContent: "Yay",
            ThemeDescription: "",
            AspectRatio: "16:9",
            HasPortrait: false));

        prompt.Should().NotContain("0 years old");
        prompt.Should().NotContain("aged 0");
    }

    [Fact]
    public void Baptism_opener_mentions_baptism_and_pastel_tone()
    {
        var prompt = _sut.BuildPrompt(new BannerPromptInput(
            Category: BannerTemplateCategory.Baptism,
            Language: "nb",
            PersonName: "Lillebror",
            PersonAge: 1,
            TextContent: "Velkommen til dåp",
            ThemeDescription: "",
            AspectRatio: "16:9",
            HasPortrait: true));

        prompt.Should().Contain("baptism");
        prompt.Should().Contain("dåp");
        prompt.Should().Contain("\"Velkommen til dåp\"");
        prompt.Should().Contain("preserve the face from the reference image");
    }

    [Theory]
    [InlineData(BannerTemplateCategory.Birthday,     true)]
    [InlineData(BannerTemplateCategory.Confirmation, true)]
    [InlineData(BannerTemplateCategory.Baptism,      true)]
    [InlineData(BannerTemplateCategory.Wedding,      true)]
    [InlineData(BannerTemplateCategory.Anniversary,  false)]
    [InlineData(BannerTemplateCategory.Christmas,    false)]
    [InlineData(BannerTemplateCategory.NewYear,      false)]
    [InlineData(BannerTemplateCategory.Other,        false)]
    public void IsPersonCentred_marks_celebration_categories_correctly(
        BannerTemplateCategory category, bool expected)
    {
        category.IsPersonCentred().Should().Be(expected);
    }

    [Fact]
    public void Escapes_quotes_in_text_content()
    {
        var prompt = _sut.BuildPrompt(new BannerPromptInput(
            Category: BannerTemplateCategory.Birthday,
            Language: "nb",
            PersonName: "Ola",
            PersonAge: 10,
            TextContent: "He said \"hi\"",
            ThemeDescription: "",
            AspectRatio: "16:9",
            HasPortrait: false));

        prompt.Should().Contain("\\\"hi\\\"");
    }
}
