using System.Net;
using System.Text;
using BannerShop.Api.Services.DesignRequests;
using BannerShop.Api.Services.DesignRequests.Fal;
using BannerShop.Api.Services.SystemSettings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BannerShop.Tests;

public sealed class FalAiImageServiceTests
{
    [Fact]
    public async Task Text_generation_uses_flux_2_pro_with_key_and_requested_ratio()
    {
        var png = await MakePngAsync(32, 18);
        var handler = new StubHandler(_ => JsonResponse(
            $$"""{"images":[{"url":"data:image/png;base64,{{Convert.ToBase64String(png)}}" }],"seed":42}"""));
        var service = CreateService(handler);

        var result = await service.GenerateAsync(
            new AiImageRequest("party banner", "16:9", null), CancellationToken.None);

        try
        {
            handler.RequestUri!.ToString().Should().Be("https://fal.run/fal-ai/flux-2-pro");
            handler.Authorization.Should().Be("Key test-fal-key");
            handler.Body.Should().Contain("\"prompt\":\"party banner\"");
            handler.Body.Should().Contain("\"width\":2048");
            handler.Body.Should().Contain("\"height\":1152");
            handler.Body.Should().Contain("\"output_format\":\"png\"");
            result.WidthPx.Should().Be(32);
            result.HeightPx.Should().Be(18);
            (await File.ReadAllBytesAsync(result.AbsolutePath)).Should().Equal(png);
        }
        finally
        {
            File.Delete(result.AbsolutePath);
        }
    }

    [Fact]
    public async Task Portrait_generation_uses_edit_endpoint_and_embeds_reference_as_data_uri()
    {
        var inputPng = await MakePngAsync(2, 2);
        var outputPng = await MakePngAsync(4, 2);
        var portrait = Path.Combine(Path.GetTempPath(), $"fal_portrait_{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(portrait, inputPng);
        var handler = new StubHandler(_ => JsonResponse(
            $$"""{"images":[{"url":"data:image/png;base64,{{Convert.ToBase64String(outputPng)}}" }]}"""));
        var service = CreateService(handler);

        try
        {
            var result = await service.GenerateAsync(
                new AiImageRequest("include this person", "2:1", portrait), CancellationToken.None);
            try
            {
                handler.RequestUri!.AbsolutePath.Should().Be("/fal-ai/flux-2-pro/edit");
                handler.Body.Should().Contain("\"image_urls\":[\"data:image/png;base64,");
            }
            finally
            {
                File.Delete(result.AbsolutePath);
            }
        }
        finally
        {
            File.Delete(portrait);
        }
    }

    [Fact]
    public async Task Safety_failure_maps_to_existing_moderation_sentinel()
    {
        var handler = new StubHandler(_ => JsonResponse(
            """{"detail":"Request blocked by the safety checker."}""",
            HttpStatusCode.UnprocessableEntity));
        var service = CreateService(handler);

        var action = () => service.GenerateAsync(
            new AiImageRequest("blocked", "16:9", null), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("moderation_block: Request blocked by the safety checker.");
    }

    [Fact]
    public async Task Billing_failure_maps_to_fal_quota_sentinel()
    {
        var handler = new StubHandler(_ => JsonResponse(
            """{"detail":"Account balance is exhausted."}""",
            HttpStatusCode.PaymentRequired));
        var service = CreateService(handler);

        var action = () => service.GenerateAsync(
            new AiImageRequest("banner", "16:9", null), CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("fal_quota_exceeded");
    }

    [Fact]
    public async Task Missing_key_returns_placeholder_without_network_call()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("network should not be called"));
        var service = CreateService(handler, apiKey: null);

        var result = await service.GenerateAsync(
            new AiImageRequest("placeholder", "4:1", null), CancellationToken.None);

        try
        {
            handler.RequestUri.Should().BeNull();
            result.WidthPx.Should().Be(2048);
            result.HeightPx.Should().Be(512);
            File.Exists(result.AbsolutePath).Should().BeTrue();
        }
        finally
        {
            File.Delete(result.AbsolutePath);
        }
    }

    private static FalAiImageService CreateService(StubHandler handler, string? apiKey = "test-fal-key")
    {
        var options = new StaticOptionsMonitor<FalOptions>(new FalOptions());
        return new FalAiImageService(
            new HttpClient(handler),
            options,
            new StubSettings(apiKey),
            NullLogger<FalAiImageService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(
        string body,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static async Task<byte[]> MakePngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, Color.CornflowerBlue);
        await using var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        return stream.ToArray();
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.TryGetValues("Authorization", out var values)
                ? values.Single()
                : null;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }

    private sealed class StubSettings(string? apiKey) : ISystemSettingsService
    {
        public Task<string?> GetValueAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(key == "fal_api_key" ? apiKey : null);

        public Task SetValueAsync(string key, string value, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<SystemSettingDto>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SystemSettingDto>>([]);
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
