namespace BannerShop.Api.Services.DesignRequests;

/// <summary>
/// Inputs accepted by an AI image generator. Implementations should produce one
/// image at the requested aspect ratio (16:9 native; 18:9 will be cropped after).
/// </summary>
public record AiImageRequest(
    string Prompt,
    string AspectRatio,           // "16:9" or "18:9" — implementations target the closest native size
    string? ReferenceImagePath    // absolute path to an uploaded portrait, optional
);

public record AiImageResult(string AbsolutePath, int WidthPx, int HeightPx);

/// <summary>
/// Provider-agnostic AI image generator. Lives behind an interface so we can
/// swap OpenAI for Azure OpenAI / Stability AI / etc. without touching callers.
/// See BANNERSH-18 for the locked-in v1 provider (OpenAI direct, gpt-image-2).
/// </summary>
public interface IAiImageService
{
    /// <summary>
    /// Generate an image for the given prompt. The returned file is the implementation's
    /// raw output — caller is responsible for cropping/post-processing.
    /// </summary>
    Task<AiImageResult> GenerateAsync(AiImageRequest request, CancellationToken ct);
}
