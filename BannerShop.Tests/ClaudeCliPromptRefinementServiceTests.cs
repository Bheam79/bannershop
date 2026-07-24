using BannerShop.Api.Services.DesignRequests;
using BannerShop.Api.Services.DesignRequests.Claude;
using BannerShop.Api.Services.SystemSettings;
using BannerShop.Core.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BannerShop.Tests;

public sealed class ClaudeCliPromptRefinementServiceTests
{
    [Fact]
    public async Task Sends_editable_main_and_category_prompts_with_all_customer_inputs()
    {
        var runner = new StubRunner("A vivid finished banner prompt");
        var settings = new StubSettings(new Dictionary<string, string>
        {
            [ClaudeCliPromptRefinementService.TokenSetting] = "oauth-secret",
            [ClaudeCliPromptRefinementService.MainPromptSetting] = "CUSTOM SYSTEM ART DIRECTION",
            [ClaudeCliPromptRefinementService.SettingKeyFor(BannerTemplateCategory.Birthday)] =
                "CUSTOM BIRTHDAY DIRECTION"
        });
        var sut = CreateService(runner, settings);

        var result = await sut.RefineAsync(MakeInput(), CancellationToken.None);

        result.Should().Be("A vivid finished banner prompt");
        runner.Token.Should().Be("oauth-secret");
        runner.SystemPrompt.Should().Be("CUSTOM SYSTEM ART DIRECTION");
        runner.UserPrompt.Should().Contain("CUSTOM BIRTHDAY DIRECTION");
        runner.UserPrompt.Should().Contain("Name: Lady Diana");
        runner.UserPrompt.Should().Contain("Age: 8");
        runner.UserPrompt.Should().Contain("Exact text on banner: Happy birthday!");
        runner.UserPrompt.Should().Contain("Theme / style input: red and blue city superhero");
        runner.UserPrompt.Should().Contain("@image1");
        runner.UserPrompt.Should().Contain("Aspect ratio: 18:9");
        runner.UserPrompt.Should().Contain("image itself is the banner");
    }

    [Fact]
    public async Task Uses_seed_defaults_when_prompt_settings_are_absent()
    {
        var runner = new StubRunner("refined");
        var settings = new StubSettings(new Dictionary<string, string>
        {
            [ClaudeCliPromptRefinementService.TokenSetting] = "oauth-secret"
        });
        var sut = CreateService(runner, settings);

        await sut.RefineAsync(MakeInput(), CancellationToken.None);

        runner.SystemPrompt.Should().Contain("banner-within-a-banner");
        runner.SystemPrompt.Should().Contain("premium designed graphic composition");
        runner.UserPrompt.Should().Contain("celebratory birthday banner");
    }

    [Fact]
    public async Task Returns_base_prompt_when_token_is_missing()
    {
        var runner = new StubRunner("should not run");
        var sut = CreateService(runner, new StubSettings(new Dictionary<string, string>()));

        var result = await sut.RefineAsync(MakeInput(), CancellationToken.None);

        result.Should().Be("deterministic fallback");
        runner.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Returns_base_prompt_when_cli_fails()
    {
        var runner = new StubRunner(new InvalidOperationException("claude unavailable"));
        var settings = new StubSettings(new Dictionary<string, string>
        {
            [ClaudeCliPromptRefinementService.TokenSetting] = "oauth-secret"
        });
        var sut = CreateService(runner, settings);

        var result = await sut.RefineAsync(MakeInput(), CancellationToken.None);

        result.Should().Be("deterministic fallback");
    }

    [Fact]
    public async Task Strips_markdown_fence_from_cli_output()
    {
        var runner = new StubRunner("```text\nA vivid prompt\n```");
        var settings = new StubSettings(new Dictionary<string, string>
        {
            [ClaudeCliPromptRefinementService.TokenSetting] = "oauth-secret"
        });
        var sut = CreateService(runner, settings);

        var result = await sut.RefineAsync(MakeInput(), CancellationToken.None);

        result.Should().Be("A vivid prompt");
    }

    private static ClaudeCliPromptRefinementService CreateService(
        IClaudeCliRunner runner,
        ISystemSettingsService settings) =>
        new(runner, settings, NullLogger<ClaudeCliPromptRefinementService>.Instance);

    private static PromptRefinementInput MakeInput() =>
        new(
            Category: BannerTemplateCategory.Birthday,
            Language: "en",
            PersonName: "Lady Diana",
            PersonAge: 8,
            TextContent: "Happy birthday!",
            ThemeDescription: "red and blue city superhero",
            AspectRatio: "18:9",
            HasPortrait: true,
            BasePrompt: "deterministic fallback");

    private sealed class StubRunner : IClaudeCliRunner
    {
        private readonly string? _result;
        private readonly Exception? _error;

        public StubRunner(string result) => _result = result;
        public StubRunner(Exception error) => _error = error;

        public int CallCount { get; private set; }
        public string? SystemPrompt { get; private set; }
        public string? UserPrompt { get; private set; }
        public string? Token { get; private set; }

        public Task<string> RunAsync(
            string systemPrompt,
            string userPrompt,
            string oauthToken,
            CancellationToken ct)
        {
            CallCount++;
            SystemPrompt = systemPrompt;
            UserPrompt = userPrompt;
            Token = oauthToken;
            return _error is not null
                ? Task.FromException<string>(_error)
                : Task.FromResult(_result!);
        }
    }

    private sealed class StubSettings(IReadOnlyDictionary<string, string> values)
        : ISystemSettingsService
    {
        public Task<string?> GetValueAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(values.TryGetValue(key, out var value) ? value : null);

        public Task SetValueAsync(string key, string value, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<SystemSettingDto>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SystemSettingDto>>([]);
    }
}
