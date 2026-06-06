using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace BannerShop.Api.Services.DesignRequests.OpenAi;

/// <summary>
/// OpenAI image generation backed by /v1/images/generations (no portrait) and
/// /v1/images/edits (when a reference portrait is supplied). Returns the model's
/// native 4K output saved as a PNG to a temp file under the system temp dir; the
/// caller decides where to copy/move it permanently.
///
/// Per BANNERSH-18: model "gpt-image-2", quality "high".
/// </summary>
public sealed class OpenAiImageService : IAiImageService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly OpenAiOptions _opts;
    private readonly ILogger<OpenAiImageService> _log;

    public OpenAiImageService(HttpClient http, IOptions<OpenAiOptions> opts, ILogger<OpenAiImageService> log)
    {
        _http = http;
        _opts = opts.Value;
        _log = log;

        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(_opts.BaseUrl);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);
        if (!string.IsNullOrWhiteSpace(_opts.OrgId))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("OpenAI-Organization", _opts.OrgId);
        _http.Timeout = TimeSpan.FromSeconds(_opts.TimeoutSeconds);
    }

    public async Task<AiImageResult> GenerateAsync(AiImageRequest request, CancellationToken ct)
    {
        var size = NativeSizeFor(request.AspectRatio);

        // ── Either /v1/images/edits (with portrait) or /v1/images/generations ──
        byte[] pngBytes;
        if (!string.IsNullOrWhiteSpace(request.ReferenceImagePath) && File.Exists(request.ReferenceImagePath))
            pngBytes = await EditWithReferenceAsync(request.Prompt, request.ReferenceImagePath, size, ct);
        else
            pngBytes = await GenerateAsync(request.Prompt, size, ct);

        // Persist to a temp file; caller copies to its final storage location.
        var tempPath = Path.Combine(Path.GetTempPath(), $"openai_{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(tempPath, pngBytes, ct);

        return new AiImageResult(tempPath, size.Width, size.Height);
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private async Task<byte[]> GenerateAsync(string prompt, NativeSize size, CancellationToken ct)
    {
        var payload = new
        {
            model = _opts.ImageModel,
            prompt,
            quality = _opts.ImageQuality,
            size = size.AsApiString(),
            n = 1,
            response_format = "b64_json"
        };

        using var resp = await _http.PostAsJsonAsync("/v1/images/generations", payload, Json, ct);
        var body = await ReadOrThrowAsync(resp, ct);

        var parsed = JsonSerializer.Deserialize<OpenAiImageResponse>(body, Json)
            ?? throw new InvalidOperationException("OpenAI: empty response body.");
        return ExtractFirstImageBytes(parsed);
    }

    private async Task<byte[]> EditWithReferenceAsync(string prompt, string referenceAbsolutePath, NativeSize size, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(_opts.ImageModel), "model");
        content.Add(new StringContent(prompt), "prompt");
        content.Add(new StringContent(_opts.ImageQuality), "quality");
        content.Add(new StringContent(size.AsApiString()), "size");
        content.Add(new StringContent("1"), "n");
        content.Add(new StringContent("b64_json"), "response_format");

        var bytes = await File.ReadAllBytesAsync(referenceAbsolutePath, ct);
        var imagePart = new ByteArrayContent(bytes);
        imagePart.Headers.ContentType = new MediaTypeHeaderValue(GuessMime(referenceAbsolutePath));
        content.Add(imagePart, "image", Path.GetFileName(referenceAbsolutePath));

        using var resp = await _http.PostAsync("/v1/images/edits", content, ct);
        var body = await ReadOrThrowAsync(resp, ct);

        var parsed = JsonSerializer.Deserialize<OpenAiImageResponse>(body, Json)
            ?? throw new InvalidOperationException("OpenAI: empty response body.");
        return ExtractFirstImageBytes(parsed);
    }

    private async Task<string> ReadOrThrowAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogError("OpenAI {Status}: {Body}", (int)resp.StatusCode, Trunc(body, 1500));
            throw new InvalidOperationException($"OpenAI image API failed ({(int)resp.StatusCode}): {Trunc(body, 300)}");
        }
        return body;
    }

    private static byte[] ExtractFirstImageBytes(OpenAiImageResponse response)
    {
        var first = response.Data?.FirstOrDefault()
            ?? throw new InvalidOperationException("OpenAI returned no image data.");
        if (!string.IsNullOrEmpty(first.B64Json))
            return Convert.FromBase64String(first.B64Json);
        throw new InvalidOperationException("OpenAI image response did not contain b64_json.");
    }

    private static string GuessMime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png"            => "image/png",
        ".webp"           => "image/webp",
        _                 => "application/octet-stream"
    };

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    /// <summary>
    /// gpt-image-2 native output sizes: we ask for 16:9 (3840x2160) for both
    /// "16:9" and "18:9" inputs (18:9 is cropped by the caller after the fact).
    /// </summary>
    private static NativeSize NativeSizeFor(string aspectRatio) => new(3840, 2160);

    private readonly record struct NativeSize(int Width, int Height)
    {
        public string AsApiString() => $"{Width}x{Height}";
    }

    private sealed class OpenAiImageResponse
    {
        [JsonPropertyName("data")]
        public List<OpenAiImageDatum>? Data { get; set; }
    }

    private sealed class OpenAiImageDatum
    {
        [JsonPropertyName("b64_json")]
        public string? B64Json { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }
}
