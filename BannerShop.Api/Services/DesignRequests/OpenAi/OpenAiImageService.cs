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
/// BANNERSH-161: The API key is read EXCLUSIVELY from the database
/// (system_settings.openai_api_key) — there is no appsettings fallback any
/// more. If the row is blank or a placeholder, a solid-colour placeholder PNG
/// is returned instead of calling the OpenAI API (solid-colour placeholder),
/// so the service can always be registered even before the admin enters a key.
///
/// BANNERSH-127: ImageModel + ImageQuality keep the DB-first / appsettings-fallback
/// resolution — they are non-secret tuning knobs (low/medium/high/auto and the
/// model id) and remain settable via either appsettings or the admin panel.
/// </summary>
public sealed class OpenAiImageService : IAiImageService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<OpenAiOptions> _optsMonitor;
    private readonly ISystemSettingsService _settings;
    private readonly ILogger<OpenAiImageService> _log;

    public OpenAiImageService(
        HttpClient http,
        IOptionsMonitor<OpenAiOptions> optsMonitor,
        ISystemSettingsService settings,
        ILogger<OpenAiImageService> log)
    {
        _http = http;
        _optsMonitor = optsMonitor;
        _settings = settings;
        _log = log;

        var opts = optsMonitor.CurrentValue;
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(opts.BaseUrl);
        // Auth header is applied per-request so a key updated at runtime takes
        // effect immediately (see GetEffectiveApiKeyAsync).
        if (!string.IsNullOrWhiteSpace(opts.OrgId))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("OpenAI-Organization", opts.OrgId);
        _http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);

        // BANNERSH-98 / BANNERSH-161: confirm at startup that the real
        // OpenAI-backed service is wired into DI. The API key itself lives in
        // the database (system_settings.openai_api_key) and is resolved per-call.
        _log.LogInformation(
            "OpenAiImageService constructed. BaseUrl={BaseUrl} ImageModel={Model} ImageQuality={Quality} " +
            "TimeoutSeconds={Timeout} (API key is read from db:openai_api_key per request)",
            opts.BaseUrl, opts.ImageModel, opts.ImageQuality, opts.TimeoutSeconds);
    }

    public async Task<AiImageResult> GenerateAsync(AiImageRequest request, CancellationToken ct)
    {
        // ── Resolve API key at runtime ─────────────────────────────────────────
        // GetEffectiveApiKeyAsync reads IOptionsMonitor.CurrentValue so it always
        // reflects the live configuration — including any appsettings.Local.json
        // changes that took effect after startup (reloadOnChange: true).
        var apiKeyOpt = await GetEffectiveApiKeyAsync(ct);
        if (apiKeyOpt is null)
        {
            // BANNERSH-161: appsettings fallback is gone; the key lives only in
            // the DB. Loud Error-level log so it is impossible to miss in
            // journalctl when the operator wonders "why is it still drawing a
            // solid colour?".
            _log.LogError(
                "OpenAI API key NOT CONFIGURED — falling back to PLACEHOLDER image (solid-colour PNG). " +
                "DB setting 'openai_api_key' = {DbKeyStatus}. " +
                "Fix: enter the key via /admin/settings; no service restart is required.",
                DescribeKey(await _settings.GetValueAsync("openai_api_key", ct)));
            return await GeneratePlaceholderAsync(request, ct);
        }

        var apiKey = apiKeyOpt.Value;
        var liveOpts = _optsMonitor.CurrentValue;

        // BANNERSH-127: resolve ImageModel + ImageQuality DB-first, same precedence
        // as ApiKey. Captured once per request so we log exactly what we send and
        // can surface the source in the log line.
        var (model, modelSrc) = await ResolveModelAsync(liveOpts, ct);
        var (quality, qualitySrc) = await ResolveQualityAsync(liveOpts, ct);

        _log.LogInformation(
            "OpenAI image generation: invoking real API. KeySource={Source} KeyPrefix={Prefix} " +
            "Model={Model} ({ModelSrc}) Quality={Quality} ({QualitySrc}) HasPortrait={HasPortrait} AspectRatio={AspectRatio}",
            apiKey.Source, MaskKey(apiKey.Key),
            model, modelSrc, quality, qualitySrc,
            !string.IsNullOrWhiteSpace(request.ReferenceImagePath) && File.Exists(request.ReferenceImagePath),
            request.AspectRatio);

        // Set auth header with the current key.
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Key);

        var size = NativeSizeFor(request.AspectRatio);

        // ── Either /v1/images/edits (with portrait) or /v1/images/generations ──
        byte[] pngBytes;
        if (!string.IsNullOrWhiteSpace(request.ReferenceImagePath) && File.Exists(request.ReferenceImagePath))
            pngBytes = await EditWithReferenceAsync(request.Prompt, request.ReferenceImagePath, model, quality, size, ct);
        else
            pngBytes = await GenerateAsync(request.Prompt, model, quality, size, ct);

        // Persist to a temp file; caller copies to its final storage location.
        var tempPath = Path.Combine(Path.GetTempPath(), $"openai_{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(tempPath, pngBytes, ct);

        _log.LogInformation(
            "OpenAI image generation: success. Bytes={Bytes} SavedTo={Path} Size={Width}x{Height}",
            pngBytes.Length, tempPath, size.Width, size.Height);

        return new AiImageResult(tempPath, size.Width, size.Height);
    }

    // ── Key resolution ────────────────────────────────────────────────────────

    /// <summary>
    /// Source the effective API key came from — used in log messages so the
    /// operator can see at a glance whether the DB admin-panel value or the
    /// appsettings value was selected.
    /// </summary>
    private readonly record struct ResolvedKey(string Key, string Source);

    /// <summary>
    /// Returns the effective OpenAI API key from the database, or null if not
    /// configured. BANNERSH-161: appsettings is no longer consulted for this
    /// key — set it via the admin settings panel.
    /// </summary>
    private async Task<ResolvedKey?> GetEffectiveApiKeyAsync(CancellationToken ct)
    {
        var dbKey = await _settings.GetValueAsync("openai_api_key", ct);
        _log.LogDebug("OpenAI key probe: DB 'openai_api_key' -> {Status}", DescribeKey(dbKey));
        if (!string.IsNullOrWhiteSpace(dbKey) && !IsPlaceholderKey(dbKey))
            return new ResolvedKey(dbKey, "db:openai_api_key");

        return null;
    }

    /// <summary>
    /// Returns the effective image model + a label describing where it came
    /// from (db / appsettings). Mirrors <see cref="GetEffectiveApiKeyAsync"/>.
    /// </summary>
    private async Task<(string Value, string Source)> ResolveModelAsync(OpenAiOptions liveOpts, CancellationToken ct)
    {
        var dbModel = await _settings.GetValueAsync("openai_image_model", ct);
        if (!string.IsNullOrWhiteSpace(dbModel))
            return (dbModel.Trim(), "db:openai_image_model");
        return (liveOpts.ImageModel, "appsettings:OpenAi:ImageModel");
    }

    /// <summary>
    /// Returns the effective image quality ("low" / "medium" / "high" / "auto")
    /// + a label describing where it came from. Same DB-first precedence as the
    /// model and the API key.
    /// </summary>
    private async Task<(string Value, string Source)> ResolveQualityAsync(OpenAiOptions liveOpts, CancellationToken ct)
    {
        var dbQuality = await _settings.GetValueAsync("openai_image_quality", ct);
        if (!string.IsNullOrWhiteSpace(dbQuality))
            return (dbQuality.Trim(), "db:openai_image_quality");
        return (liveOpts.ImageQuality, "appsettings:OpenAi:ImageQuality");
    }

    private static bool IsPlaceholderKey(string key) =>
        key.StartsWith("sk-REPLACE", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Produces a short, safe human description of an API-key value for logs:
    /// "unset" / "placeholder(sk-REPLACE…)" / "set(sk-prj…1A2B, 51 chars)".
    /// Never leaks the secret middle of the key.
    /// </summary>
    private static string DescribeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "unset";
        if (IsPlaceholderKey(key)) return $"placeholder({MaskKey(key)})";
        return $"set({MaskKey(key)}, {key.Length} chars)";
    }

    /// <summary>
    /// Returns "abcd…wxyz" — first 6 chars + last 4 chars — so the operator can
    /// recognise the key in logs without exposing the secret portion. Very short
    /// keys (≤10 chars) are masked entirely.
    /// </summary>
    private static string MaskKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "(empty)";
        if (key.Length <= 10) return new string('*', key.Length);
        return key[..6] + "…" + key[^4..];
    }

    /// <summary>
    /// Generates a solid-colour placeholder PNG when no API key is configured.
    /// </summary>
    private async Task<AiImageResult> GeneratePlaceholderAsync(AiImageRequest request, CancellationToken ct)
    {
        var size = NativeSizeFor(request.AspectRatio);
        var tempPath = Path.Combine(Path.GetTempPath(), $"placeholder_{Guid.NewGuid():N}.png");

        var hash = unchecked((uint)request.Prompt.GetHashCode());
        var tint = new Rgba32(
            (byte)(40 + (hash & 0xFF) / 2),
            (byte)(40 + ((hash >> 8) & 0xFF) / 2),
            (byte)(60 + ((hash >> 16) & 0xFF) / 3),
            255);

        using var img = new Image<Rgba32>(size.Width, size.Height, tint);
        await img.SaveAsPngAsync(tempPath, ct);

        _log.LogWarning(
            "Generated solid-colour PLACEHOLDER PNG ({Width}x{Height}, rgb={R},{G},{B}) at {Path} — " +
            "real OpenAI API was NOT called. See preceding error for the reason.",
            size.Width, size.Height, tint.R, tint.G, tint.B, tempPath);

        return new AiImageResult(tempPath, size.Width, size.Height);
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private async Task<byte[]> GenerateAsync(string prompt, string model, string quality, NativeSize size, CancellationToken ct)
    {
        var payload = new
        {
            model,
            prompt,
            quality,
            size = size.AsApiString(),
            n = 1
            // response_format is not supported by gpt-image-* models; they always
            // return b64_json by default.  Older DALL-E endpoints accepted this
            // parameter, but sending it to gpt-image-1/gpt-image-2 causes a 400
            // "Unknown parameter" error.
        };

        using var resp = await _http.PostAsJsonAsync("/v1/images/generations", payload, Json, ct);
        var body = await ReadOrThrowAsync(resp, ct);

        var parsed = JsonSerializer.Deserialize<OpenAiImageResponse>(body, Json)
            ?? throw new InvalidOperationException("OpenAI: empty response body.");
        return ExtractFirstImageBytes(parsed);
    }

    private async Task<byte[]> EditWithReferenceAsync(string prompt, string referenceAbsolutePath, string model, string quality, NativeSize size, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(model), "model");
        content.Add(new StringContent(prompt), "prompt");
        content.Add(new StringContent(quality), "quality");
        content.Add(new StringContent(size.AsApiString()), "size");
        content.Add(new StringContent("1"), "n");
        // response_format omitted — gpt-image-* models always return b64_json and
        // reject the parameter with a 400 "Unknown parameter" error.

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
    /// Maps an aspect-ratio string to the best native size for gpt-image-2.
    ///
    /// gpt-image-2 accepts arbitrary WxH where both dimensions are divisible by 16,
    /// the ratio is between 1:3 and 3:1, and the max resolution is 3840×2160.
    ///
    /// Accepts either:
    ///   • Legacy ratio labels: "1:1", "1:2", "2:1", "3:1", "4:1", "16:9", "18:9".
    ///   • Frontend WxH strings (BANNERSH-170): e.g. "267x150", "150x150", "90x180" —
    ///     the actual print dimensions chosen by the customer. We compute the W/H
    ///     ratio, clamp to the API's 1:3 — 3:1 limit, and pick the closest
    ///     16-aligned native size so the AI image MATCHES the customer's choice.
    ///
    /// Bug fixed (BANNERSH-175): previously any WxH input fell through to the
    /// default 16:9 (1792×1008) — the ratio buttons in the wizard had no effect
    /// on the generated image.
    ///
    /// 18:9 (legacy) and 16:9 both use a true 16:9 native size — the 18:9 center-crop
    /// in <c>AiGenerationPipeline</c> still runs for old requests that carry that value.
    /// 4:1 is outside the 3:1 API limit and is clamped to 3:1.
    /// </summary>
    private static NativeSize NativeSizeFor(string aspectRatio)
    {
        // Fast path for the legacy ratio labels — these keep their hand-tuned sizes
        // so old DesignRequests still produce identical output to before.
        switch ((aspectRatio ?? string.Empty).Trim())
        {
            case "1:1":            return new NativeSize(1024, 1024);
            case "1:2":            return new NativeSize(1024, 2048);
            case "2:1":            return new NativeSize(2048, 1024);
            case "3:1":            return new NativeSize(3072, 1024);
            case "4:1":            return new NativeSize(3072, 1024); // clamped to 3:1
            case "16:9":
            case "18:9":           return new NativeSize(1792, 1008);
        }

        // BANNERSH-175: parse anything else as W/H, clamp to API limit, snap to /16.
        var ratio = ParseRatio(aspectRatio);
        ratio = Math.Clamp(ratio, 1.0 / 3.0, 3.0);

        const int maxSide = 2048; // headroom under the 3840×2160 cap
        int width, height;
        if (ratio >= 1.0)
        {
            width  = maxSide;
            height = (int)Math.Round(maxSide / ratio);
        }
        else
        {
            height = maxSide;
            width  = (int)Math.Round(maxSide * ratio);
        }
        // Snap each side to the nearest multiple of 16, never below 16.
        // Rounding to nearest (rather than truncating down) keeps the resulting
        // ratio under the API's 3:1 limit at the boundary — e.g. for input "3:1"
        // (=ratio 3.0) the short side becomes 688 instead of 672, giving 2.977
        // rather than 3.048.
        width  = Math.Max(16, ((width  + 8) / 16) * 16);
        height = Math.Max(16, ((height + 8) / 16) * 16);
        return new NativeSize(width, height);
    }

    /// <summary>
    /// Parses an aspect-ratio string into a numeric W/H ratio. Accepts the
    /// "A:B" label form and the "WxH" dimensions form (case-insensitive).
    /// Falls back to 16/9 when the input is empty or unparseable.
    /// </summary>
    private static double ParseRatio(string? aspectRatio)
    {
        if (string.IsNullOrWhiteSpace(aspectRatio)) return 16.0 / 9.0;
        var s = aspectRatio.Trim();

        var xIdx = s.IndexOfAny(['x', 'X']);
        if (xIdx > 0
            && int.TryParse(s[..xIdx], out var w)
            && int.TryParse(s[(xIdx + 1)..], out var h)
            && w > 0 && h > 0)
            return (double)w / h;

        var colonIdx = s.IndexOf(':');
        if (colonIdx > 0
            && int.TryParse(s[..colonIdx], out var a)
            && int.TryParse(s[(colonIdx + 1)..], out var b)
            && a > 0 && b > 0)
            return (double)a / b;

        return 16.0 / 9.0;
    }

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
