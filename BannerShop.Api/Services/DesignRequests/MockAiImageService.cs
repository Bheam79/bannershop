using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BannerShop.Api.Services.DesignRequests;

/// <summary>
/// Dev / test fallback used when the OpenAI API key is not configured.
/// Generates a simple solid-colour PNG so the rest of the pipeline (cropping,
/// storage, status transitions) is exercisable end-to-end without burning real
/// API credit.
/// </summary>
public sealed class MockAiImageService : IAiImageService
{
    private readonly ILogger<MockAiImageService> _log;

    public MockAiImageService(ILogger<MockAiImageService> log)
    {
        _log = log;
        _log.LogWarning(
            "MockAiImageService is in use — OpenAI credentials are not configured. AI images will be placeholders.");
    }

    public async Task<AiImageResult> GenerateAsync(AiImageRequest request, CancellationToken ct)
    {
        const int Width = 1920;
        const int Height = 1080;
        var tempPath = Path.Combine(Path.GetTempPath(), $"mockai_{Guid.NewGuid():N}.png");

        // Tint colour derived from the prompt hash so consecutive previews look distinct.
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
}
