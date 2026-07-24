using System.Net;
using System.Text;
using BannerShop.Api.Services.DesignRequests;
using BannerShop.Api.Services.DesignRequests.OpenAi;
using BannerShop.Api.Services.SystemSettings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Exercises <see cref="OpenAiImageService"/>'s HTTP-error translation
/// (<c>ReadOrThrowAsync</c>) by driving the real service through a stub
/// <see cref="HttpMessageHandler"/>. BANNERSH-287 introduced two
/// distinguishable sentinels stored in <c>DesignRequest.LastError</c>:
/// <c>moderation_block[: reason]</c> (400) and <c>openai_quota_exceeded</c>
/// (429 insufficient_quota); everything else falls through to a generic
/// "OpenAI image API failed (status)" message. Those sentinels drive the
/// customer-facing copy + admin diagnostics, so lock the parsing down here —
/// the pipeline tests only mock <see cref="IAiImageService"/> with a
/// pre-formed exception and never reach this JSON to sentinel mapping.
/// </summary>
public class OpenAiImageServiceErrorTests
{
    // -- moderation_block (400) -----------------------------------------------

    [Fact]
    public async Task ModerationBlock_with_message_appends_openai_reason_to_sentinel()
    {
        var stub = new StubHandler(HttpStatusCode.BadRequest, body:
            """
            {
              "error": {
                "code": "moderation_block",
                "message": "This request was rejected as a result of our safety system. Image descriptions generated from your request may contain text that is not allowed by our safety system."
              }
            }
            """);
        var sut = CreateService(stub);

        var act = () => sut.GenerateAsync(MakeRequest(), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("moderation_block: This request was rejected*");
        // The image-generations endpoint (no portrait) must have been hit.
        stub.LastRequestUri!.AbsolutePath.Should().Be("/v1/images/generations");
    }

    [Fact]
    public async Task ModerationBlock_without_message_uses_bare_sentinel()
    {
        var stub = new StubHandler(HttpStatusCode.BadRequest, body:
            """{ "error": { "code": "moderation_block" } }""");
        var sut = CreateService(stub);

        var act = () => sut.GenerateAsync(MakeRequest(), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("moderation_block");
    }

    [Fact]
    public async Task ModerationBlock_on_portrait_edit_flows_through_images_edits_endpoint()
    {
        // The most realistic moderation trigger for this app is a portrait being
        // flagged for depicting a real, identifiable person during /v1/images/edits.
        var portrait = Path.Combine(Path.GetTempPath(), $"portrait_{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(portrait, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        try
        {
            var stub = new StubHandler(HttpStatusCode.BadRequest, body:
                """{ "error": { "code": "moderation_block", "message": "Your input image may contain a real person." } }""");
            var sut = CreateService(stub);

            var act = () => sut.GenerateAsync(MakeRequest(referenceImagePath: portrait), CancellationToken.None);

            (await act.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage("moderation_block: Your input image may contain a real person.");
            stub.LastRequestUri!.AbsolutePath.Should().Be("/v1/images/edits");
        }
        finally
        {
            File.Delete(portrait);
        }
    }

    [Fact]
    public async Task BadRequest_with_non_moderation_code_falls_through_to_generic_error()
    {
        var stub = new StubHandler(HttpStatusCode.BadRequest, body:
            """{ "error": { "code": "unknown_parameter", "message": "Unknown parameter: response_format." } }""");
        var sut = CreateService(stub);

        var act = () => sut.GenerateAsync(MakeRequest(), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("OpenAI image API failed (400):*")
            .Which.Message.Should().NotContain("moderation_block");
    }

    [Fact]
    public async Task BadRequest_with_non_json_body_falls_through_to_generic_error()
    {
        var stub = new StubHandler(HttpStatusCode.BadRequest, body: "not json at all");
        var sut = CreateService(stub);

        var act = () => sut.GenerateAsync(MakeRequest(), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("OpenAI image API failed (400):*");
    }

    // -- insufficient_quota (429) ---------------------------------------------

    [Fact]
    public async Task InsufficientQuota_maps_to_quota_sentinel()
    {
        var stub = new StubHandler((HttpStatusCode)429, body:
            """
            {
              "error": {
                "code": "insufficient_quota",
                "message": "You exceeded your current quota, please check your plan and billing details."
              }
            }
            """);
        var sut = CreateService(stub);

        var act = () => sut.GenerateAsync(MakeRequest(), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("openai_quota_exceeded");
    }

    [Fact]
    public async Task RateLimited_without_quota_code_falls_through_to_generic_error()
    {
        // A plain 429 rate-limit (not billing exhaustion) must NOT be reported as
        // openai_quota_exceeded -- that sentinel points the operator at billing.
        var stub = new StubHandler((HttpStatusCode)429, body:
            """{ "error": { "code": "rate_limit_exceeded", "message": "Rate limit reached." } }""");
        var sut = CreateService(stub);

        var act = () => sut.GenerateAsync(MakeRequest(), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("OpenAI image API failed (429):*")
            .Which.Message.Should().NotContain("openai_quota_exceeded");
    }

    // -- other failures --------------------------------------------------------

    [Fact]
    public async Task ServerError_falls_through_to_generic_error()
    {
        var stub = new StubHandler(HttpStatusCode.InternalServerError, body: "upstream boom");
        var sut = CreateService(stub);

        var act = () => sut.GenerateAsync(MakeRequest(), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("OpenAI image API failed (500):*");
    }

    // -- success path ----------------------------------------------------------

    [Fact]
    public async Task Success_writes_b64_image_to_temp_file_and_returns_native_size()
    {
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var b64 = Convert.ToBase64String(pngBytes);
        var stub = new StubHandler(HttpStatusCode.OK, body:
            $$"""{ "data": [ { "b64_json": "{{b64}}" } ] }""");
        var sut = CreateService(stub);

        var result = await sut.GenerateAsync(MakeRequest(aspectRatio: "16:9"), CancellationToken.None);

        try
        {
            // NativeSizeFor("16:9") -> 1792x1008 (see OpenAiImageService).
            result.WidthPx.Should().Be(1792);
            result.HeightPx.Should().Be(1008);
            File.Exists(result.AbsolutePath).Should().BeTrue();
            (await File.ReadAllBytesAsync(result.AbsolutePath)).Should().Equal(pngBytes);
            stub.LastAuthHeader.Should().Be("Bearer sk-test-key");
        }
        finally
        {
            File.Delete(result.AbsolutePath);
        }
    }

    [Fact]
    public async Task No_api_key_configured_returns_placeholder_without_calling_openai()
    {
        // BANNERSH-161: a blank db:openai_api_key must degrade to a solid-colour
        // placeholder PNG rather than throwing or hitting the network.
        var stub = new StubHandler(HttpStatusCode.OK, body: "should-not-be-called");
        var sut = CreateService(stub, apiKey: null);

        var result = await sut.GenerateAsync(MakeRequest(aspectRatio: "1:1"), CancellationToken.None);

        try
        {
            result.WidthPx.Should().Be(1024);
            result.HeightPx.Should().Be(1024);
            File.Exists(result.AbsolutePath).Should().BeTrue();
            stub.LastRequestUri.Should().BeNull("the OpenAI API must not be called when no key is configured");
        }
        finally
        {
            File.Delete(result.AbsolutePath);
        }
    }

    // -- Helpers ---------------------------------------------------------------

    private static AiImageRequest MakeRequest(string aspectRatio = "16:9", string? referenceImagePath = null)
        => new(Prompt: "a festive birthday banner", AspectRatio: aspectRatio, ReferenceImagePath: referenceImagePath);

    private static OpenAiImageService CreateService(StubHandler handler, string? apiKey = "sk-test-key")
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.test") };
        var monitor = new SimpleOptionsMonitor<OpenAiOptions>(new OpenAiOptions
        {
            ImageModel = "gpt-image-2",
            ImageQuality = "high",
            BaseUrl = "https://api.openai.test",
            TimeoutSeconds = 5
        });
        // BANNERSH-161: API key comes from ISystemSettingsService (db:openai_api_key).
        var settings = new StubSettings(apiKey);
        return new OpenAiImageService(http, monitor, settings, NullLogger<OpenAiImageService>.Instance);
    }

    /// <summary>
    /// Minimal <see cref="ISystemSettingsService"/> returning a single canned
    /// value for "openai_api_key" (null =&gt; key unset =&gt; placeholder path).
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

    private sealed class SimpleOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>
    /// Minimal <see cref="HttpMessageHandler"/> returning a canned response and
    /// recording the last request URI / Authorization header for assertions.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public Uri? LastRequestUri { get; private set; }
        public string? LastAuthHeader { get; private set; }

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastAuthHeader = request.Headers.Authorization?.ToString();

            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }
}
