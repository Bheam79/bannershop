namespace BannerShop.Api.Services.DesignRequests;

/// <summary>
/// Placeholder implementation that throws when invoked. v1 of the AI pipeline never
/// calls <see cref="IPhotoCompositor"/> — it relies on gpt-image-2's reference-image
/// edit endpoint. See BANNERSH-18 §3.
/// </summary>
public sealed class PhotoCompositorNotImplemented : IPhotoCompositor
{
    public Task<string> CompositeAsync(string backgroundAbsolutePath, string portraitAbsolutePath, CancellationToken ct = default)
        => throw new NotImplementedException(
            "Server-side photo compositing is not enabled in v1 — gpt-image-2 handles portraits natively via /v1/images/edits.");
}
