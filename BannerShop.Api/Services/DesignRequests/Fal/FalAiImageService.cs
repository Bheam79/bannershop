using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BannerShop.Api.Services.SystemSettings;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BannerShop.Api.Services.DesignRequests.Fal;

/// <summary>
/// Banner image generation through fal.ai's FLUX.2 [pro] text-to-image and
/// image-edit endpoints.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "fal.ai HTTP wrapper; network behavior is covered with a stub handler")]
public sealed class FalAiImageService : IAiImageService
{
    private const string ApiKeySetting = "fal_api_key";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<FalOptions> _options;
    private readonly ISystemSettingsService _settings;
    private readonly ILogger<FalAiImageService> _log;

    public FalAiImageService(
        HttpClient http,
        IOptionsMonitor<FalOptions> options,
        ISystemSettingsService settings,
        ILogger<FalAiImageService> log)
    {
        _http = http;
        _options = options;
        _settings = settings;
        _log = log;

        var current = options.CurrentValue;
        _http.Timeout = TimeSpan.FromSeconds(current.TimeoutSeconds);
        _log.LogInformation(
            "FalAiImageService constructed. BaseUrl={BaseUrl} Model={Model} TimeoutSeconds={Timeout} " +
            "(API key is read from db:{ApiKeySetting} per request)",
            current.BaseUrl, current.ModelId, current.TimeoutSeconds, ApiKeySetting);
    }

    public async Task<AiImageResult> GenerateAsync(AiImageRequest request, CancellationToken ct)
    {
        var apiKey = await _settings.GetValueAsync(ApiKeySetting, ct);
        if (string.IsNullOrWhiteSpace(apiKey) || IsPlaceholderKey(apiKey))
        {
            _log.LogError(
                "fal.ai API key NOT CONFIGURED — falling back to a placeholder image. " +
                "Enter fal_api_key via /admin/settings; no restart is required.");
            return await GeneratePlaceholderAsync(request, ct);
        }

        var options = _options.CurrentValue;
        var size = NativeSizeFor(request.AspectRatio);
        var hasReference = !string.IsNullOrWhiteSpace(request.ReferenceImagePath)
            && File.Exists(request.ReferenceImagePath);
        var endpoint = BuildEndpoint(options, hasReference);

        object payload;
        if (hasReference)
        {
            var referencePath = request.ReferenceImagePath!;
            var bytes = await File.ReadAllBytesAsync(referencePath, ct);
            var dataUri = $"data:{GuessMime(referencePath)};base64,{Convert.ToBase64String(bytes)}";
            payload = new
            {
                prompt = request.Prompt,
                image_size = new { width = size.Width, height = size.Height },
                image_urls = new[] { dataUri },
                safety_tolerance = "2",
                enable_safety_checker = true,
                output_format = "png",
                sync_mode = false
            };
        }
        else
        {
            payload = new
            {
                prompt = request.Prompt,
                image_size = new { width = size.Width, height = size.Height },
                safety_tolerance = "2",
                enable_safety_checker = true,
                output_format = "png",
                sync_mode = false
            };
        }

        _log.LogInformation(
            "fal.ai image generation: Model={Model} HasPortrait={HasPortrait} " +
            "AspectRatio={AspectRatio} RequestedSize={Width}x{Height}",
            options.ModelId, hasReference, request.AspectRatio, size.Width, size.Height);

        using var apiRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload, options: Json)
        };
        apiRequest.Headers.TryAddWithoutValidation("Authorization", $"Key {apiKey.Trim()}");

        using var response = await _http.SendAsync(apiRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            ThrowFalError(response.StatusCode, body);

        var parsed = JsonSerializer.Deserialize<FalImageResponse>(body, Json)
            ?? throw new InvalidOperationException("fal.ai returned an empty response.");
        var imageUrl = parsed.Images?.FirstOrDefault()?.Url;
        if (string.IsNullOrWhiteSpace(imageUrl))
            throw new InvalidOperationException("fal.ai returned no image URL.");

        var imageBytes = await DownloadImageAsync(imageUrl, ct);
        var info = Image.Identify(imageBytes)
            ?? throw new InvalidOperationException("fal.ai returned an unreadable image.");
        var tempPath = Path.Combine(Path.GetTempPath(), $"fal_{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(tempPath, imageBytes, ct);

        _log.LogInformation(
            "fal.ai image generation succeeded. Bytes={Bytes} Size={Width}x{Height} SavedTo={Path}",
            imageBytes.Length, info.Width, info.Height, tempPath);
        return new AiImageResult(tempPath, info.Width, info.Height);
    }

    private async Task<byte[]> DownloadImageAsync(string imageUrl, CancellationToken ct)
    {
        if (imageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = imageUrl.IndexOf(',');
            if (comma < 0)
                throw new InvalidOperationException("fal.ai returned a malformed image data URI.");
            return Convert.FromBase64String(imageUrl[(comma + 1)..]);
        }

        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new InvalidOperationException("fal.ai returned an invalid image URL.");

        // Deliberately do not attach the fal API key to the media-storage request.
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"fal.ai image download failed ({(int)response.StatusCode}).");
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static Uri BuildEndpoint(FalOptions options, bool edit)
    {
        var baseUrl = options.BaseUrl.TrimEnd('/');
        var model = options.ModelId.Trim('/');
        return new Uri($"{baseUrl}/{model}{(edit ? "/edit" : string.Empty)}");
    }

    [DoesNotReturn]
    private void ThrowFalError(HttpStatusCode statusCode, string body)
    {
        var detail = ExtractErrorDetail(body);
        _log.LogError("fal.ai {Status}: {Body}", (int)statusCode, Truncate(body, 1500));

        if (LooksLikeModerationFailure(detail))
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(detail) ? "moderation_block" : $"moderation_block: {detail}");

        if ((statusCode is HttpStatusCode.PaymentRequired or HttpStatusCode.TooManyRequests)
            && LooksLikeQuotaFailure(detail))
            throw new InvalidOperationException("fal_quota_exceeded");

        throw new InvalidOperationException(
            $"fal.ai image API failed ({(int)statusCode}): {Truncate(detail ?? body, 300)}");
    }

    private static string? ExtractErrorDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            foreach (var name in new[] { "detail", "message", "error" })
            {
                if (!root.TryGetProperty(name, out var value))
                    continue;
                if (value.ValueKind == JsonValueKind.String)
                    return value.GetString();
                return value.GetRawText();
            }
        }
        catch (JsonException)
        {
            // Return the raw response below.
        }
        return body;
    }

    private static bool LooksLikeModerationFailure(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return false;
        return detail.Contains("safety", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("moderation", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("nsfw", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("content policy", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeQuotaFailure(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return false;
        return detail.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("balance", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("credit", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("billing", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<AiImageResult> GeneratePlaceholderAsync(
        AiImageRequest request,
        CancellationToken ct)
    {
        var size = NativeSizeFor(request.AspectRatio);
        var path = Path.Combine(Path.GetTempPath(), $"placeholder_{Guid.NewGuid():N}.png");
        var hash = unchecked((uint)request.Prompt.GetHashCode());
        var tint = new Rgba32(
            (byte)(40 + (hash & 0xFF) / 2),
            (byte)(40 + ((hash >> 8) & 0xFF) / 2),
            (byte)(60 + ((hash >> 16) & 0xFF) / 3));
        using var image = new Image<Rgba32>(size.Width, size.Height, tint);
        await image.SaveAsPngAsync(path, ct);
        return new AiImageResult(path, size.Width, size.Height);
    }

    private static NativeSize NativeSizeFor(string? aspectRatio)
    {
        var ratio = Math.Clamp(ParseRatio(aspectRatio), 1.0 / 4.0, 4.0);
        const int maxSide = 2048;
        int width;
        int height;
        if (ratio >= 1)
        {
            width = maxSide;
            height = (int)Math.Round(maxSide / ratio);
        }
        else
        {
            height = maxSide;
            width = (int)Math.Round(maxSide * ratio);
        }

        width = Math.Clamp(((width + 8) / 16) * 16, 512, maxSide);
        height = Math.Clamp(((height + 8) / 16) * 16, 512, maxSide);
        return new NativeSize(width, height);
    }

    private static double ParseRatio(string? aspectRatio)
    {
        if (string.IsNullOrWhiteSpace(aspectRatio))
            return 16.0 / 9.0;
        var value = aspectRatio.Trim();
        var separator = value.IndexOfAny([':', 'x', 'X']);
        if (separator > 0
            && double.TryParse(value[..separator], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
            && double.TryParse(value[(separator + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
            && width > 0
            && height > 0)
            return width / height;
        return 16.0 / 9.0;
    }

    private static string GuessMime(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "image/png"
        };

    private static bool IsPlaceholderKey(string key) =>
        key.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value ?? string.Empty : value[..max] + "…";

    private readonly record struct NativeSize(int Width, int Height);

    private sealed class FalImageResponse
    {
        [JsonPropertyName("images")]
        public List<FalImage>? Images { get; set; }
    }

    private sealed class FalImage
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }
}
