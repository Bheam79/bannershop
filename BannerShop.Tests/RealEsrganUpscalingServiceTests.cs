using System.Net;
using System.Text;
using System.Text.Json;
using BannerShop.Api.Services.DesignRequests.Replicate;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Exercises <see cref="RealEsrganUpscalingService"/> (BANNERSH-57) against a
/// scripted <see cref="HttpMessageHandler"/> covering the create/poll/download
/// round-trip with Replicate's prediction API. The class is
/// <c>[ExcludeFromCodeCoverage]</c> ("tested via integration") but the request
/// building, status-driven poll loop, and error-message formatting are pure
/// regressable logic that had zero test references before this file.
/// </summary>
public class RealEsrganUpscalingServiceTests
{
    // -- input validation (no HTTP call) ---------------------------------------

    [Fact]
    public async Task Missing_input_file_throws_without_calling_replicate()
    {
        var handler = new ScriptedHandler(_ => throw new InvalidOperationException("should not be called"));
        var sut = CreateService(handler);

        var act = () => sut.UpscaleAsync(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.png"));

        await act.Should().ThrowAsync<FileNotFoundException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_api_token_throws_without_calling_replicate()
    {
        var input = MakeTempFile(".png", new byte[] { 1 });
        try
        {
            var handler = new ScriptedHandler(_ => throw new InvalidOperationException("should not be called"));
            var sut = CreateService(handler, apiToken: "");

            var act = () => sut.UpscaleAsync(input);

            (await act.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage("Replicate API token is not configured.");
            handler.Requests.Should().BeEmpty();
        }
        finally
        {
            File.Delete(input);
        }
    }

    // -- create-prediction errors -----------------------------------------------

    [Fact]
    public async Task Create_prediction_non_success_throws_with_status_and_body()
    {
        var input = MakeTempFile(".png", new byte[] { 1 });
        try
        {
            var handler = new ScriptedHandler(_ => Json(HttpStatusCode.Unauthorized, """{ "detail": "Invalid token." }"""));
            var sut = CreateService(handler);

            var act = () => sut.UpscaleAsync(input);

            (await act.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage("Replicate API call failed (401):*Invalid token*");
        }
        finally
        {
            File.Delete(input);
        }
    }

    [Fact]
    public async Task Create_prediction_response_missing_id_throws()
    {
        var input = MakeTempFile(".png", new byte[] { 1 });
        try
        {
            var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, """{ "status": "starting" }"""));
            var sut = CreateService(handler);

            var act = () => sut.UpscaleAsync(input);

            (await act.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage("Replicate: create-prediction response missing id.");
        }
        finally
        {
            File.Delete(input);
        }
    }

    [Fact]
    public async Task Create_prediction_payload_includes_pinned_version_scale_and_data_uri_mime()
    {
        var input = MakeTempFile(".jpg", new byte[] { 1, 2, 3 });
        string? outPath = null;
        try
        {
            var handler = new ScriptedHandler(req => req.Method == HttpMethod.Post
                ? Json(HttpStatusCode.OK, """{ "id": "pred_7" }""")
                : Json(HttpStatusCode.OK, """{ "id": "pred_7", "status": "succeeded", "output": "https://replicate.delivery/out/r.png" }"""));
            var sut = CreateService(handler);

            outPath = await sut.UpscaleAsync(input, scale: 2);

            var createReq = handler.Requests.Single(r => r.Method == HttpMethod.Post);
            var body = await createReq.Content!.ReadAsStringAsync();
            body.Should().Contain("\"scale\":2");
            body.Should().Contain("\"version\":\"f121d640bd286e1fdc67f9799164c1d5be36ff74576ee11c803ae5b665dd46aa\"");
            body.Should().Contain("\"image\":\"data:image/jpeg;base64,");
        }
        finally
        {
            File.Delete(input);
            if (outPath is not null) File.Delete(outPath);
        }
    }

    // -- poll status handling -----------------------------------------------------

    [Fact]
    public async Task Poll_failed_status_surfaces_replicate_error()
    {
        var input = MakeTempFile(".png", new byte[] { 1 });
        try
        {
            var handler = new ScriptedHandler(req => req.Method == HttpMethod.Post
                ? Json(HttpStatusCode.OK, """{ "id": "pred_2" }""")
                : Json(HttpStatusCode.OK, """{ "id": "pred_2", "status": "failed", "error": "NSFW content detected." }"""));
            var sut = CreateService(handler);

            var act = () => sut.UpscaleAsync(input);

            (await act.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage("Replicate prediction pred_2 failed: NSFW content detected.");
        }
        finally
        {
            File.Delete(input);
        }
    }

    [Fact]
    public async Task Poll_canceled_status_throws()
    {
        var input = MakeTempFile(".png", new byte[] { 1 });
        try
        {
            var handler = new ScriptedHandler(req => req.Method == HttpMethod.Post
                ? Json(HttpStatusCode.OK, """{ "id": "pred_3" }""")
                : Json(HttpStatusCode.OK, """{ "id": "pred_3", "status": "canceled" }"""));
            var sut = CreateService(handler);

            var act = () => sut.UpscaleAsync(input);

            (await act.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage("Replicate prediction pred_3 was canceled.");
        }
        finally
        {
            File.Delete(input);
        }
    }

    [Fact]
    public async Task Poll_exceeding_max_seconds_throws_timeout()
    {
        var input = MakeTempFile(".png", new byte[] { 1 });
        try
        {
            var handler = new ScriptedHandler(req => req.Method == HttpMethod.Post
                ? Json(HttpStatusCode.OK, """{ "id": "pred_4" }""")
                : Json(HttpStatusCode.OK, """{ "id": "pred_4", "status": "processing" }"""));
            var sut = CreateService(handler, maxPollSeconds: 0, pollIntervalMs: 5);

            var act = () => sut.UpscaleAsync(input);

            (await act.Should().ThrowAsync<TimeoutException>())
                .WithMessage("Replicate prediction pred_4 did not complete within 0s.");
        }
        finally
        {
            File.Delete(input);
        }
    }

    [Fact]
    public async Task Poll_retries_while_processing_then_succeeds()
    {
        var input = MakeTempFile(".png", new byte[] { 1 });
        var outputBytes = new byte[] { 9, 9, 9, 9 };
        var pollCalls = 0;
        string? outPath = null;
        try
        {
            var handler = new ScriptedHandler(req =>
            {
                if (req.Method == HttpMethod.Post) return Json(HttpStatusCode.OK, """{ "id": "pred_8" }""");
                if (req.RequestUri!.Host == "replicate.delivery")
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(outputBytes) };
                pollCalls++;
                return pollCalls < 3
                    ? Json(HttpStatusCode.OK, """{ "id": "pred_8", "status": "processing" }""")
                    : Json(HttpStatusCode.OK, """{ "id": "pred_8", "status": "succeeded", "output": "https://replicate.delivery/out/r.png" }""");
            });
            var sut = CreateService(handler, maxPollSeconds: 30, pollIntervalMs: 5);

            outPath = await sut.UpscaleAsync(input);

            pollCalls.Should().Be(3);
            (await File.ReadAllBytesAsync(outPath)).Should().Equal(outputBytes);
        }
        finally
        {
            File.Delete(input);
            if (outPath is not null) File.Delete(outPath);
        }
    }

    // -- output extraction + download ---------------------------------------------

    [Fact]
    public async Task Succeeded_without_usable_output_throws()
    {
        var input = MakeTempFile(".png", new byte[] { 1 });
        try
        {
            var handler = new ScriptedHandler(req => req.Method == HttpMethod.Post
                ? Json(HttpStatusCode.OK, """{ "id": "pred_6" }""")
                : Json(HttpStatusCode.OK, """{ "id": "pred_6", "status": "succeeded" }"""));
            var sut = CreateService(handler);

            var act = () => sut.UpscaleAsync(input);

            (await act.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage("Replicate prediction pred_6 completed without a usable output URL.");
        }
        finally
        {
            File.Delete(input);
        }
    }

    [Fact]
    public async Task Succeeded_with_array_output_uses_first_element()
    {
        var input = MakeTempFile(".png", new byte[] { 1 });
        var outputBytes = new byte[] { 4, 5, 6 };
        string? outPath = null;
        try
        {
            var handler = new ScriptedHandler(req =>
            {
                if (req.Method == HttpMethod.Post) return Json(HttpStatusCode.OK, """{ "id": "pred_9" }""");
                if (req.RequestUri!.Host == "replicate.delivery")
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(outputBytes) };
                return Json(HttpStatusCode.OK,
                    """{ "id": "pred_9", "status": "succeeded", "output": ["https://replicate.delivery/out/first.webp", "https://replicate.delivery/out/second.webp"] }""");
            });
            var sut = CreateService(handler);

            outPath = await sut.UpscaleAsync(input);

            Path.GetExtension(outPath).Should().Be(".webp");
            (await File.ReadAllBytesAsync(outPath)).Should().Equal(outputBytes);
        }
        finally
        {
            File.Delete(input);
            if (outPath is not null) File.Delete(outPath);
        }
    }

    [Theory]
    [InlineData("https://replicate.delivery/out/result.png?x=1", ".png")]
    [InlineData("https://replicate.delivery/out/result.webp", ".webp")]
    [InlineData("https://replicate.delivery/out/result", ".png")]
    [InlineData("https://replicate.delivery/out/result.tiff", ".png")]
    public async Task Output_extension_is_inferred_from_url_with_unknown_types_defaulting_to_png(
        string outputUrl, string expectedExtension)
    {
        var input = MakeTempFile(".png", new byte[] { 1 });
        string? outPath = null;
        try
        {
            var handler = new ScriptedHandler(req =>
            {
                if (req.Method == HttpMethod.Post) return Json(HttpStatusCode.OK, """{ "id": "pred_10" }""");
                if (req.RequestUri!.Host == "replicate.delivery")
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 7 }) };
                return Json(HttpStatusCode.OK,
                    $$"""{ "id": "pred_10", "status": "succeeded", "output": "{{outputUrl}}" }""");
            });
            var sut = CreateService(handler);

            outPath = await sut.UpscaleAsync(input);

            Path.GetExtension(outPath).Should().Be(expectedExtension);
        }
        finally
        {
            File.Delete(input);
            if (outPath is not null) File.Delete(outPath);
        }
    }

    [Fact]
    public async Task Download_failure_throws_with_status_and_url()
    {
        var input = MakeTempFile(".png", new byte[] { 1 });
        try
        {
            var handler = new ScriptedHandler(req =>
            {
                if (req.Method == HttpMethod.Post) return Json(HttpStatusCode.OK, """{ "id": "pred_5" }""");
                if (req.RequestUri!.Host == "replicate.delivery")
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                return Json(HttpStatusCode.OK,
                    """{ "id": "pred_5", "status": "succeeded", "output": "https://replicate.delivery/out/result.png" }""");
            });
            var sut = CreateService(handler);

            var act = () => sut.UpscaleAsync(input);

            (await act.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage("Replicate output download failed (404): https://replicate.delivery/out/result.png");
        }
        finally
        {
            File.Delete(input);
        }
    }

    // -- Helpers ------------------------------------------------------------------

    private static string MakeTempFile(string extension, byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"realesrgan_test_{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static RealEsrganUpscalingService CreateService(
        HttpMessageHandler handler, string apiToken = "r8-test-token", int maxPollSeconds = 5, int pollIntervalMs = 5)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.replicate.test") };
        var opts = Options.Create(new ReplicateOptions
        {
            ApiToken = apiToken,
            BaseUrl = "https://api.replicate.test",
            TimeoutSeconds = 5,
            PollIntervalMs = pollIntervalMs,
            MaxPollSeconds = maxPollSeconds
        });
        return new RealEsrganUpscalingService(http, opts, NullLogger<RealEsrganUpscalingService>.Instance);
    }

    /// <summary>
    /// Routes requests to a caller-supplied responder and records every request
    /// sent, so tests can dispatch on method/host/path across the multi-step
    /// create/poll/download flow.
    /// </summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }
}
