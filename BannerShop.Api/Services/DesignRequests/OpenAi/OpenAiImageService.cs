using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BannerShop.Api.Services.SystemSettings;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BannerShop.Api.Services.DesignRequests.OpenAi;

/// <summary>
/// OpenAI image generation backed by /v1/images/generations (no portrait) and
/// /v1/images/edits (when a reference portrait is supplied). Returns the model's
/// native 4K output saved as a PNG to a temp file under the system temp dir; the
/// caller decides where to copy/move it permanently.
///
/// Per BANNERSH-18: model "gpt-image-2", quality "high".
///
/// BANNERSH-98: The API key is resolved at call time (not at startup):
///   1. Database system settings ("openai_api_key") — set via the admin panel.
///   2. Fallback to OpenAi:ApiKey in appsettings.
/// If neither yields a non-placeholder key, a solid-colour placeholder PNG is
/// returned instead of calling the OpenAI API, mirroring MockAiImageService.
/// This means the service can always be registered regardless of whether the
/// key was available at startup.
/// </summary>
public sealed class OpenAiImageService : IAiImageService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly OpenAiOptions _opts;
    private readonly ISystemSettingsService _settings;
    private readonly ILogger<OpenAiImageService> _log;

    public OpenAiImageService(
        HttpClient http,
        IOptions<OpenAiOptions> opts,
        ISystemSettingsService settings,
        ILogger<OpenAiImageService> log)
    {
        _http = http;
        _opts = opts.Value;
        _settings = settings;
        _log = log;

        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(_opts.BaseUrl);
        // Auth header is applied per-request so a key updated at runtime takes
        // effect immediately (see GetEffectiveApiKeyAsync).
        if (!string.IsNullOrWhiteSpace(_opts.OrgId))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("OpenAI-Organization", _opts.OrgId);
        _http.Timeout = TimeSpan.FromSeconds(_opts.TimeoutSeconds);
    }

    public async Task<AiImageResult> GenerateAsync(AiImageRequest request, CancellationToken ct)
    {
        // ── Resolve API key at runtime ─────────────────────────────────────────
        var apiKey = await GetEffectiveApiKeyAsync(ct);
        if (apiKey is null)
        {
            _log.LogWarning(
                "OpenAI API key is not configured (neither in admin settings nor in appsettings). " +
                "Returning placeholder image. Configure the key at /admin/settings.");
            return await GeneratePlaceholderAsync(request, ct);
        }

        // Set auth header with the current key.
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

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

    // ── Key resolution ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the effective OpenAI API key, preferring the DB-stored value
    /// (set via admin panel) over the appsettings.json value. Returns null if
    /// no usable key is found.
    /// </summary>
    private async Task<string?> GetEffectiveApiKeyAsync(CancellationToken ct)
    {
        // 1. Database setting (admin panel) — always checked first.
        var dbKey = await _settings.GetValueAsync("openai_api_key", ct);
        if (!string.IsNullOrWhiteSpace(dbKey) && !IsPlaceholderKey(dbKey))
            return dbKey;

        // 2. Config-file value (appsettings*.json / env vars / Makefile).
        var cfgKey = _opts.ApiKey;
        if (!string.IsNullOrWhiteSpace(cfgKey) && !IsPlaceholderKey(cfgKey))
            return cfgKey;

        return null;
    }

    private static bool IsPlaceholderKey(string key) =>
        key.StartsWith("sk-REPLACE", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Generates a solid-colour placeholder PNG (same as MockAiImageService)
    /// when no API key is configured.
    /// </summary>
    private static async Task<AiImageResult> GeneratePlaceholderAsync(AiImageRequest request, CancellationToken ct)
    {
        const int Width = 1920;
        const int Height = 1080;
        var tempPath = Path.Combine(Path.GetTempPath(), $"placeholder_{Guid.NewGuid():N}.png");

        var hash = unchecked((uint)request.Prompt.GetHashCode());
        var tint = new Rgba32(
            (byte)(40 + (hash & 0xFF) / 2),
            (byte)(40 + ((hash >> 8) & 0xFF) / 2),
            (byte)(60 + ((hash >> 16) & 0xFF) / 3),
            255);

        using var img = new Image<Rgba32>(Width, Height, tint);
        await img.SaveAsPngAsync(tempPath, ct);
        return new AiImageResult(tempPath, Width, Height);
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
