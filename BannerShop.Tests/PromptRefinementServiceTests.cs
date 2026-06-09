using System.Net;
using System.Text;
using BannerShop.Api.Services.DesignRequests;
using BannerShop.Api.Services.DesignRequests.OpenAi;
using BannerShop.Api.Services.SystemSettings;
using BannerShop.Core.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BannerShop.Tests;

public class PromptRefinementServiceTests
{
    // ── Noop refiner ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Noop_returns_base_prompt_unchanged()
    {
        var sut = new NoopPromptRefinementService();
        var input = MakeInput(basePrompt: "the base prompt", theme: "minecraft");

        var refined = await sut.RefineAsync(input, CancellationToken.None);

        refined.Should().Be("the base prompt");
    }

    // ── OpenAI refiner ───────────────────────────────────────────────────────

    [Fact]
    public async Task OpenAi_returns_first_choice_message_content_on_success()
    {
        var stub = new StubHandler(HttpStatusCode.OK, body:
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "Create a vivid Minecraft-themed birthday banner with @image1 the photo of the celebrant embedded; match the colour and lighting of the photo to the blocky landscape, render the text 'Gratulerer med 7!' in bold pixel-style Norwegian typography, ultra-wide 2:1 landscape, photorealistic-meets-Minecraft, no watermarks, no logos."
                  }
                }
              ]
            }
            """);
        var sut = CreateOpenAiService(stub);

        var refined = await sut.RefineAsync(
            MakeInput(theme: "minecraft", basePrompt: "boring base prompt"),
            CancellationToken.None);

        refined.Should().Contain("Minecraft");
        refined.Should().Contain("@image1");
        refined.Should().NotContain("boring base prompt");
        stub.LastRequestUri!.AbsolutePath.Should().Be("/v1/chat/completions");
        // Authorization header must carry the configured key.
        stub.LastAuthHeader.Should().Be("Bearer sk-test-key");
    }

    [Fact]
    public async Task OpenAi_falls_back_to_base_prompt_on_http_error()
    {
        var stub = new StubHandler(HttpStatusCode.InternalServerError, body: "boom");
        var sut = CreateOpenAiService(stub);

        var refined = await sut.RefineAsync(
            MakeInput(basePrompt: "deterministic fallback"),
            CancellationToken.None);

        refined.Should().Be("deterministic fallback");
    }

    [Fact]
    public async Task OpenAi_falls_back_to_base_prompt_on_empty_content()
    {
        var stub = new StubHandler(HttpStatusCode.OK, body:
            """{ "choices": [ { "message": { "role": "assistant", "content": "" } } ] }""");
        var sut = CreateOpenAiService(stub);

        var refined = await sut.RefineAsync(
            MakeInput(basePrompt: "deterministic fallback"),
            CancellationToken.None);

        refined.Should().Be("deterministic fallback");
    }

    [Fact]
    public async Task OpenAi_strips_surrounding_code_fences_from_model_output()
    {
        var stub = new StubHandler(HttpStatusCode.OK, body:
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "```\nthe actual refined prompt body\n```"
                  }
                }
              ]
            }
            """);
        var sut = CreateOpenAiService(stub);

        var refined = await sut.RefineAsync(
            MakeInput(basePrompt: "irrelevant base"),
            CancellationToken.None);

        refined.Should().Be("the actual refined prompt body");
    }

    [Fact]
    public async Task OpenAi_falls_back_to_base_prompt_on_network_exception()
    {
        var stub = new StubHandler(throwOnSend: new HttpRequestException("connection refused"));
        var sut = CreateOpenAiService(stub);

        var refined = await sut.RefineAsync(
            MakeInput(basePrompt: "kept as fallback"),
            CancellationToken.None);

        refined.Should().Be("kept as fallback");
    }

    [Fact]
    public async Task OpenAi_sends_category_theme_and_overlay_text_to_the_chat_model()
    {
        // The stub records the request body so we can assert the customer's
        // terse theme + verbatim overlay text were forwarded to the LLM.
        var stub = new StubHandler(HttpStatusCode.OK, body:
            """{ "choices": [ { "message": { "role": "assistant", "content": "ok" } } ] }""");
        var sut = CreateOpenAiService(stub);

        var input = MakeInput(theme: "minecraft", textContent: "Gratulerer med 7!", category: BannerTemplateCategory.Birthday);
        await sut.RefineAsync(input, CancellationToken.None);

        stub.LastRequestBody.Should().NotBeNull();
        var body = stub.LastRequestBody!;
        body.Should().Contain("Birthday");
        body.Should().Contain("minecraft");
        body.Should().Contain("Gratulerer med 7!");
        // System prompt must instruct the model about gpt-image-2 / image edits.
        body.Should().Contain("gpt-image-2");
        body.Should().Contain("/v1/images/edits");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PromptRefinementInput MakeInput(
        string basePrompt = "base prompt",
        string theme = "tropical",
        string textContent = "Gratulerer!",
        BannerTemplateCategory category = BannerTemplateCategory.Birthday)
        => new(
            Category: category,
            Language: "nb",
            PersonName: "Ola",
            PersonAge: 7,
            TextContent: textContent,
            ThemeDescription: theme,
            AspectRatio: "16:9",
            HasPortrait: true,
            BasePrompt: basePrompt);

    private static OpenAiPromptRefinementService CreateOpenAiService(StubHandler handler, string? apiKey = "sk-test-key")
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.test") };
        var monitor = new SimpleOptionsMonitor<OpenAiOptions>(new OpenAiOptions
        {
            ChatModel = "gpt-4o-mini",
            BaseUrl = "https://api.openai.test",
            ChatTimeoutSeconds = 5
        });
        // BANNERSH-161: API key now comes from ISystemSettingsService, not OpenAiOptions.
        var settings = new StubSettings(apiKey);
        return new OpenAiPromptRefinementService(http, monitor, settings, NullLogger<OpenAiPromptRefinementService>.Instance);
    }

    /// <summary>
    /// Minimal <see cref="ISystemSettingsService"/> that returns a single
    /// canned value for "openai_api_key".
    /// </summary>
    private sealed class StubSettings : ISystemSettingsService
    {
        private readonly string? _apiKey;
        public StubSettings(string? apiKey) => _apiKey = apiKey;

        public Task<string?> GetValueAsync(string key, CancellationToken ct = default)
            => Task.FromResult(key == "openai_api_key" ? _apiKey : null);

        public Task SetValueAsync(string key, string value, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<SystemSettingDto>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SystemSettingDto>>(Array.Empty<SystemSettingDto>());
    }

    /// <summary>
    /// Minimal <see cref="IOptionsMonitor{T}"/> wrapper for unit tests.
    /// </summary>
    private sealed class SimpleOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>
    /// Minimal HttpMessageHandler that returns a canned response and records the
    /// last request URI / Authorization header / body for assertions.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly Exception? _throwOnSend;

        public Uri? LastRequestUri { get; private set; }
        public string? LastAuthHeader { get; private set; }
        public string? LastRequestBody { get; private set; }

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public StubHandler(Exception throwOnSend)
        {
            _throwOnSend = throwOnSend;
            _status = HttpStatusCode.OK;
            _body = string.Empty;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastAuthHeader = request.Headers.Authorization?.ToString();
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            if (_throwOnSend is not null)
                throw _throwOnSend;

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }
}
