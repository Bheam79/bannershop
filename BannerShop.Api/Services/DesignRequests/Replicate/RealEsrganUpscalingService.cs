using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace BannerShop.Api.Services.DesignRequests.Replicate;

/// <summary>
/// 4x image upscaler backed by Replicate's hosted <c>nightmareai/real-esrgan</c>
/// model. Per BANNERSH-57 this is exposed as an admin/order-backend operation
/// rather than wired into the customer-facing AI preview pipeline — production
/// prints benefit from the extra resolution but customers don't see it.
///
/// Implementation:
///   1. Read the input PNG/JPEG and base64-encode it as a <c>data:</c> URI
///      (Replicate accepts URLs and data URIs for image inputs).
///   2. POST /v1/predictions with the pinned model version + input.
///   3. Poll GET /v1/predictions/{id} until status is succeeded/failed/canceled.
///   4. Download the resulting upscaled image and write it to a new temp file.
///
/// The returned path is always a NEW file under the system temp dir; the caller
/// owns moving / persisting it. Failures throw <see cref="InvalidOperationException"/>
/// so the surrounding admin code can surface them to the operator.
/// </summary>
public sealed class RealEsrganUpscalingService : IUpscalingService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ReplicateOptions _opts;
    private readonly ILogger<RealEsrganUpscalingService> _log;

    public RealEsrganUpscalingService(HttpClient http, IOptions<ReplicateOptions> opts, ILogger<RealEsrganUpscalingService> log)
    {
        _http = http;
        _opts = opts.Value;
        _log = log;

        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(_opts.BaseUrl);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiToken);
        _http.Timeout = TimeSpan.FromSeconds(_opts.TimeoutSeconds);
    }

    public async Task<string> UpscaleAsync(string inputAbsolutePath, int scale = 4, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(inputAbsolutePath) || !File.Exists(inputAbsolutePath))
            throw new FileNotFoundException("Upscaler input file not found.", inputAbsolutePath);

        if (string.IsNullOrWhiteSpace(_opts.ApiToken))
            throw new InvalidOperationException("Replicate API token is not configured.");

        // 1. Read input + build a data: URI. Real-ESRGAN supports png/jpg input.
        var bytes = await File.ReadAllBytesAsync(inputAbsolutePath, ct);
        var mime = GuessMime(inputAbsolutePath);
        var dataUri = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";

        // 2. Kick off the prediction.
        var createPayload = new
        {
            version = _opts.RealEsrganModelVersion,
            input = new
            {
                image = dataUri,
                scale = scale,
                face_enhance = false
            }
        };

        using var createResp = await _http.PostAsJsonAsync("/v1/predictions", createPayload, Json, ct);
        var createBody = await ReadOrThrowAsync(createResp, ct);

        var prediction = JsonSerializer.Deserialize<ReplicatePrediction>(createBody, Json)
            ?? throw new InvalidOperationException("Replicate: empty create-prediction response.");
        if (string.IsNullOrWhiteSpace(prediction.Id))
            throw new InvalidOperationException("Replicate: create-prediction response missing id.");

        _log.LogInformation("Replicate Real-ESRGAN: prediction {Id} created for {Path} (scale=x{Scale})",
            prediction.Id, inputAbsolutePath, scale);

        // 3. Poll for completion.
        var prediction2 = await PollUntilDoneAsync(prediction.Id, ct);

        var outputUrl = prediction2.Output switch
        {
            JsonElement el when el.ValueKind == JsonValueKind.String => el.GetString(),
            JsonElement el when el.ValueKind == JsonValueKind.Array && el.GetArrayLength() > 0
                => el[0].GetString(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(outputUrl))
            throw new InvalidOperationException(
                $"Replicate prediction {prediction.Id} completed without a usable output URL.");

        // 4. Download upscaled image to a new temp file.
        var outBytes = await DownloadAsync(outputUrl, ct);
        var outPath = Path.Combine(Path.GetTempPath(),
            $"realesrgan_{Guid.NewGuid():N}{InferOutputExtension(outputUrl)}");
        await File.WriteAllBytesAsync(outPath, outBytes, ct);

        _log.LogInformation("Replicate Real-ESRGAN: prediction {Id} succeeded -> {Path} ({Bytes} bytes)",
            prediction.Id, outPath, outBytes.Length);

        return outPath;
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private async Task<ReplicatePrediction> PollUntilDoneAsync(string predictionId, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(_opts.MaxPollSeconds);
        var pollDelay = TimeSpan.FromMilliseconds(Math.Max(250, _opts.PollIntervalMs));

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            using var resp = await _http.GetAsync($"/v1/predictions/{predictionId}", ct);
            var body = await ReadOrThrowAsync(resp, ct);

            var pred = JsonSerializer.Deserialize<ReplicatePrediction>(body, Json)
                ?? throw new InvalidOperationException("Replicate: empty poll response.");

            switch ((pred.Status ?? string.Empty).ToLowerInvariant())
            {
                case "succeeded":
                    return pred;
                case "failed":
                    throw new InvalidOperationException(
                        $"Replicate prediction {predictionId} failed: {pred.Error ?? "(no error message)"}");
                case "canceled":
                    throw new InvalidOperationException(
                        $"Replicate prediction {predictionId} was canceled.");
            }

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"Replicate prediction {predictionId} did not complete within {_opts.MaxPollSeconds}s.");

            await Task.Delay(pollDelay, ct);
        }
    }

    private async Task<byte[]> DownloadAsync(string url, CancellationToken ct)
    {
        // Replicate output URLs are short-lived HTTPS links; use a fresh request
        // so we don't carry Authorization to a third-party host (replicate.delivery).
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Replicate output download failed ({(int)resp.StatusCode}): {url}");
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<string> ReadOrThrowAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogError("Replicate {Status}: {Body}", (int)resp.StatusCode, Trunc(body, 1500));
            throw new InvalidOperationException(
                $"Replicate API call failed ({(int)resp.StatusCode}): {Trunc(body, 300)}");
        }
        return body;
    }

    private static string GuessMime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png"            => "image/png",
        ".webp"           => "image/webp",
        _                 => "application/octet-stream"
    };

    private static string InferOutputExtension(string url)
    {
        // Strip query-string / fragment then look at the extension.
        var pathOnly = url;
        var q = pathOnly.IndexOf('?');
        if (q >= 0) pathOnly = pathOnly[..q];
        var ext = Path.GetExtension(pathOnly).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".webp" ? ext : ".png";
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    // ── Wire format ──────────────────────────────────────────────────────────

    private sealed class ReplicatePrediction
    {
        [JsonPropertyName("id")]     public string? Id { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("error")]  public string? Error { get; set; }

        // Output is either a string URL or an array of string URLs depending on the model.
        [JsonPropertyName("output")] public JsonElement Output { get; set; }
    }
}
