namespace BannerShop.Api.Services.BannerBuilder;

public interface IImageProcessingService
{
    /// <summary>Reads pixel dimensions of an image file at the given absolute path.</summary>
    Task<(int WidthPx, int HeightPx)> ReadDimensionsAsync(string absolutePath, CancellationToken ct);

    /// <summary>
    /// Renders the first page of a PDF to a PNG file at <paramref name="outputAbsolutePath"/>.
    /// Returns the (WidthPx, HeightPx) of the rendered image.
    /// </summary>
    Task<(int WidthPx, int HeightPx)> RenderPdfFirstPageToPngAsync(
        string pdfAbsolutePath, string outputAbsolutePath, CancellationToken ct);

    /// <summary>
    /// Produces a JPEG preview at <paramref name="outputAbsolutePath"/> from a source image,
    /// rotated by <paramref name="rotationDegrees"/> (must be 0/90/180/270) and resized so the
    /// longer side is ≤ <paramref name="maxWidth"/> pixels. Returns the preview dimensions.
    /// </summary>
    Task<(int WidthPx, int HeightPx)> GeneratePreviewAsync(
        string sourceAbsolutePath, string outputAbsolutePath,
        int rotationDegrees, int maxWidth, int quality, CancellationToken ct);

    /// <summary>
    /// Center-crops an image to the target aspect ratio expressed as
    /// <paramref name="ratioWidth"/>:<paramref name="ratioHeight"/> and writes it as PNG
    /// to <paramref name="outputAbsolutePath"/>. Returns the resulting dimensions.
    /// </summary>
    Task<(int WidthPx, int HeightPx)> CenterCropAsync(
        string sourceAbsolutePath, string outputAbsolutePath,
        int ratioWidth, int ratioHeight, CancellationToken ct);
}
