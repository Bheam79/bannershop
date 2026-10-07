using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BannerShop.Api.Services.BannerBuilder;
using SixLabors.ImageSharp;

namespace BannerShop.Api.Services.Orders;

/// <summary>
/// Admin-only download derivatives: originals and customer previews are never changed.
/// Content hash + scale keys both the durable fal queue handle and the local PNG.
/// A singleton gate coalesces concurrent clicks; saved queue handles survive restarts.
/// </summary>
public sealed class FalOrderUpscaleService(
    BannerFileStorage storage, IImageProcessingService images, IHttpClientFactory clients)
{
    public const string Model = "fal-ai/seedvr/upscale/image/seamless";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private sealed record QueueState(string? StatusUrl, string? ResponseUrl, string? Error = null);

    public async Task<string?> GetCachedPathAsync(string source, int scale, CancellationToken ct)
    {
        var path = await CachePathAsync(source, scale, ct);
        return File.Exists(path) ? path : null;
    }

    /// <returns>True when the local download is ready; false while fal is processing.</returns>
    public async Task<bool> PrepareAsync(string source, int scale, string? apiKey, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var output = await CachePathAsync(source, scale, ct);
            if (File.Exists(output)) return true; // Works even if the key was later cleared.
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new UpscaleException("Legg inn fal.ai API Key under admin-innstillinger først.", 503);

            var statePath = output + ".queue";
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var http = clients.CreateClient("FalOrderUpscale");
            QueueState state;
            if (!File.Exists(statePath))
            {
                var input = await ImageDataUriAsync(source, output, ct);
                // Do not silently charge again after an ambiguous submit timeout/crash.
                await SaveStateAsync(statePath, new(null, null,
                    "Innsending til fal.ai ble avbrutt. Kontroller fal.ai-køen før en ny jobb opprettes."));
                using var request = Authorized(HttpMethod.Post, $"https://queue.fal.run/{Model}", apiKey);
                request.Content = JsonContent.Create(new
                {
                    image_url = input, upscale_mode = "factor", upscale_factor = scale, output_format = "png"
                });
                // Once submission starts, a closed browser must not discard the returned handle.
                using var response = await http.SendAsync(request, CancellationToken.None);
                if (!response.IsSuccessStatusCode)
                {
                    // A definite client rejection did not enqueue a job. Other failures are ambiguous.
                    if ((int)response.StatusCode is 400 or 401 or 403 or 404 or 413 or 415 or 422 or 429)
                        File.Delete(statePath);
                    throw ProviderError(response.StatusCode);
                }
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                state = new(QueueUrl(json.RootElement.GetProperty("status_url").GetString()),
                    QueueUrl(json.RootElement.GetProperty("response_url").GetString()));
                await SaveStateAsync(statePath, state);
                return false;
            }

            state = JsonSerializer.Deserialize<QueueState>(await File.ReadAllTextAsync(statePath, ct))
                ?? throw new UpscaleException("Ugyldig oppskalerings-cache.");
            if (state.Error is not null) throw new UpscaleException(state.Error);
            using var statusRequest = Authorized(HttpMethod.Get, QueueUrl(state.StatusUrl), apiKey);
            using var statusResponse = await http.SendAsync(statusRequest, ct);
            if (!statusResponse.IsSuccessStatusCode) throw ProviderError(statusResponse.StatusCode);
            using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync(ct));
            if (status.RootElement.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            {
                // Keep the failed handle rather than resubmit on every download click.
                await SaveStateAsync(statePath, state with { Error = "fal.ai kunne ikke oppskalere bildet. Kontroller jobben i fal.ai." });
                throw new UpscaleException("fal.ai kunne ikke oppskalere bildet. Kontroller jobben i fal.ai.");
            }
            if (status.RootElement.GetProperty("status").GetString() != "COMPLETED") return false;

            using var resultRequest = Authorized(HttpMethod.Get, QueueUrl(state.ResponseUrl), apiKey);
            using var resultResponse = await http.SendAsync(resultRequest, ct);
            if (!resultResponse.IsSuccessStatusCode) throw ProviderError(resultResponse.StatusCode);
            using var result = JsonDocument.Parse(await resultResponse.Content.ReadAsStringAsync(ct));
            var url = result.RootElement.GetProperty("image").GetProperty("url").GetString();
            // Never forward the fal credential to the media host.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var media) || media.Scheme != "https" ||
                !(media.Host.EndsWith(".fal.media", StringComparison.OrdinalIgnoreCase) ||
                  media.Host.Equals("fal.media", StringComparison.OrdinalIgnoreCase) ||
                  media.Host.Equals("storage.googleapis.com", StringComparison.OrdinalIgnoreCase)))
                throw new UpscaleException("fal.ai returnerte en ugyldig bildeadresse.");
            var temp = output + ".tmp";
            try
            {
                using var download = await http.GetAsync(media, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!download.IsSuccessStatusCode) throw ProviderError(download.StatusCode);
                await using (var file = File.Create(temp))
                    await download.Content.CopyToAsync(file, ct);
                // Verify a valid PNG before publishing the cache; incomplete files are never reused.
                var info = await Image.IdentifyAsync(temp, ct);
                if (info.Metadata.DecodedImageFormat?.Name != "PNG")
                    throw new UpscaleException("fal.ai returnerte ikke et gyldig PNG-bilde.");
                File.Move(temp, output, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return true;
        }
        finally { _gate.Release(); }
    }

    private async Task<string> CachePathAsync(string source, int scale, CancellationToken ct)
    {
        if (scale is not (2 or 4)) throw new UpscaleException("Velg 2x eller 4x oppskalering.", 400);
        var absolute = SourcePath(source);
        if (!File.Exists(absolute)) throw new UpscaleException("Originalfilen finnes ikke.", 404);
        await using var file = File.OpenRead(absolute);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).ToLowerInvariant();
        return storage.AbsolutePathFor($"admin-upscales/seedvr-seamless-v1/{hash}-{scale}x.png");
    }

    private string SourcePath(string source)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(storage.AbsolutePathFor(""))) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(storage.AbsolutePathFor(source));
        if (!path.StartsWith(root, StringComparison.Ordinal))
            throw new UpscaleException("Ugyldig originalfil.", 400);
        return path;
    }

    private async Task<string> ImageDataUriAsync(string source, string output, CancellationToken ct)
    {
        var inputPath = SourcePath(source);
        var pdfPng = output + ".input.png";
        try
        {
            if (Path.GetExtension(inputPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                await images.RenderPdfFirstPageToPngAsync(inputPath, pdfPng, ct);
                inputPath = pdfPng;
            }
            var info = await Image.IdentifyAsync(inputPath, ct);
            var mime = info.Metadata.DecodedImageFormat?.DefaultMimeType;
            if (mime is not ("image/png" or "image/jpeg" or "image/webp"))
                throw new UpscaleException("Oppskalering støtter PNG, JPEG, WebP og PDF.", 422);
            return $"data:{mime};base64,{Convert.ToBase64String(await File.ReadAllBytesAsync(inputPath, ct))}";
        }
        finally { if (File.Exists(pdfPng)) File.Delete(pdfPng); }
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string apiKey)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Key", apiKey.Trim());
        return request;
    }

    private static string QueueUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host != "queue.fal.run" || !uri.AbsolutePath.StartsWith("/fal-ai/seedvr/", StringComparison.Ordinal))
            throw new UpscaleException("fal.ai returnerte en ugyldig køadresse.");
        return uri.AbsoluteUri;
    }

    private static UpscaleException ProviderError(HttpStatusCode status) =>
        new($"fal.ai forespørselen feilet (HTTP {(int)status}). Kontroller API-nøkkel og fal.ai-jobben.");

    private static async Task SaveStateAsync(string path, QueueState state)
    {
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(state));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}

public sealed class UpscaleException(string message, int statusCode = 502) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
