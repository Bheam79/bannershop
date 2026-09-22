using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BannerShop.Api.Services.DesignRequests.Cli;

public sealed class ParallelCliImageService(ImageCliRuntime runtime, ILogger<ParallelCliImageService> log) : IMultiCandidateImageService
{
    public async Task<AiImageResult> GenerateAsync(AiImageRequest request, CancellationToken ct)
    {
        var results = await GenerateCandidatesAsync(request, ct);
        foreach (var extra in results.Skip(1)) File.Delete(extra.AbsolutePath);
        return results[0];
    }

    public async Task<IReadOnlyList<AiImageResult>> GenerateCandidatesAsync(AiImageRequest request, CancellationToken ct)
    {
        var results = await Task.WhenAll(GenerateOneAsync("codex", request, ct), GenerateOneAsync("grok", request, ct));
        if (ct.IsCancellationRequested)
        {
            foreach (var result in results.OfType<AiImageResult>()) File.Delete(result.AbsolutePath);
            ct.ThrowIfCancellationRequested();
        }
        var valid = results.OfType<AiImageResult>().ToArray();
        if (valid.Length == 0) throw new InvalidOperationException("image_providers_failed");
        return valid;
    }

    private async Task<AiImageResult?> GenerateOneAsync(string provider, AiImageRequest request, CancellationToken ct)
    {
        try
        {
            return await runtime.WithCredentialsAsync(provider, async (root, start, token) =>
            {
                string? reference = null;
                if (request.ReferenceImagePath is not null)
                {
                    reference = Path.Combine(root, "work", "portrait.png");
                    using var portrait = await Image.LoadAsync(request.ReferenceImagePath, token);
                    // Grok single-image edits inherit the input ratio. Pad, never stretch,
                    // the portrait to a landscape canvas before submitting it.
                    var height = request.AspectRatio == "18:9" ? 768 : 864;
                    portrait.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new Size(1536, height), Mode = ResizeMode.Pad, PadColor = Color.White
                    }));
                    await portrait.SaveAsPngAsync(reference, token);
                }
                var instruction = ImageProviderPrompts.Instruction(provider, reference is not null);
                var prompt = ImageProviderPrompts.Build(provider, request, reference);

                if (provider == "codex")
                {
                    foreach (var arg in new[] { "exec", "--ephemeral", "--ignore-user-config", "--ignore-rules",
                        "--skip-git-repo-check", "--sandbox", "read-only", "--color", "never",
                        "-c", "cli_auth_credentials_store=\"file\"",
                        "-c", "features.shell_tool=false", "-c", "features.unified_exec=false",
                        "-c", "features.multi_agent=false", "-c", "features.multi_agent_v2=false",
                        "-c", "web_search=\"disabled\"", "-c", "features.image_generation=true" }) start.ArgumentList.Add(arg);
                    if (reference is not null) { start.ArgumentList.Add("--image"); start.ArgumentList.Add(reference); }
                    start.ArgumentList.Add("-");
                }
                else
                {
                    // A private home plus no shared leader avoids inheriting sessions/tools.
                    await File.WriteAllTextAsync(Path.Combine(root, "home", "config.toml"),
                        "[cli]\nuse_leader = false\n", token);
                    foreach (var arg in new[] { "--no-subagents", "--disable-web-search", "--no-plan",
                        "--tools", reference is null ? "image_gen" : "image_edit",
                        "--allow", "image_gen", "--allow", "image_edit", "--max-turns", "3",
                        "--permission-mode", "dontAsk", "--system-prompt-override", instruction,
                        "--prompt-file", Path.Combine(root, "work", "prompt.txt") }) start.ArgumentList.Add(arg);
                    await File.WriteAllTextAsync(Path.Combine(root, "work", "prompt.txt"), prompt, token);
                }
                await ImageCliRuntime.ExecuteAsync(start, provider == "codex" ? prompt : null, null, token);
                // Accept only image files created in this invocation's private home.
                // Never trust paths or URLs supplied in the model's final prose.
                var files = Directory.EnumerateFiles(Path.Combine(root, "home"), "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint
                }).Where(p => provider == "codex" ? p.Contains("/generated_images/") : p.Contains("/images/"))
                  .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp")
                  .OrderByDescending(File.GetLastWriteTimeUtc);
                foreach (var file in files)
                {
                    if (new FileInfo(file).Length is <= 0 or > 40_000_000) continue;
                    var info = await Image.IdentifyAsync(file, token);
                    if ((long)info.Width * info.Height > 32_000_000) continue;
                    using var image = await Image.LoadAsync<Rgba32>(file, token);
                    if (!IsValidBanner(image)) continue;
                    var result = Path.Combine(Path.GetTempPath(), $"banner-{provider}-{Guid.NewGuid():N}.png");
                    await image.SaveAsPngAsync(result, token);
                    return new AiImageResult(result, image.Width, image.Height, provider);
                }
                throw new InvalidOperationException("CLI returned no valid banner image.");
            }, ct);
        }
        catch (Exception ex)
        {
            // A failed, unavailable or timed-out provider cannot discard its sibling's image.
            log.LogWarning("Image provider {Provider} failed ({ErrorType}); keeping any other valid result.",
                provider, ex.GetType().Name);
            return null;
        }
    }

    private static bool IsValidBanner(Image<Rgba32> image)
    {
        if (image.Width < 512 || image.Height < 256 || image.Width <= image.Height) return false;
        var first = image[0, 0];
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
                if (image[x, y] != first) return true;
        return false;
    }
}
