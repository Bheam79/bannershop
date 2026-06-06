namespace BannerShop.Api.Services.DesignRequests;

/// <summary>
/// Stub interface for server-side compositing of a customer portrait into a
/// background image. Per BANNERSH-18 §3, v1 does NOT implement this — gpt-image-2's
/// reference-image edit endpoint preserves the portrait face automatically.
///
/// Kept as an abstraction so a future fallback path can be wired in without
/// reshuffling the pipeline.
/// </summary>
public interface IPhotoCompositor
{
    /// <summary>
    /// Composite <paramref name="portraitAbsolutePath"/> onto
    /// <paramref name="backgroundAbsolutePath"/>. Returns the absolute path of the
    /// composited result. v1 implementation throws — guard with a feature flag.
    /// </summary>
    Task<string> CompositeAsync(
        string backgroundAbsolutePath,
        string portraitAbsolutePath,
        CancellationToken ct = default);
}
