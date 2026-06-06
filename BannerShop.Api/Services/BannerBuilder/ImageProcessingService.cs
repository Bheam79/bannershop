using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace BannerShop.Api.Services.BannerBuilder;

/// <summary>
/// Image processing backed by SixLabors.ImageSharp (raster) and PDFtoImage (PDF→PNG).
/// </summary>
public sealed class ImageProcessingService : IImageProcessingService
{
    public async Task<(int WidthPx, int HeightPx)> ReadDimensionsAsync(string absolutePath, CancellationToken ct)
    {
        // Identify() reads only the header — cheap and avoids loading pixels.
        var info = await Image.IdentifyAsync(absolutePath, ct);
        return (info.Width, info.Height);
    }

    public async Task<(int WidthPx, int HeightPx)> RenderPdfFirstPageToPngAsync(
        string pdfAbsolutePath, string outputAbsolutePath, CancellationToken ct)
    {
        // PDFtoImage is synchronous and uses PDFium under the hood — wrap in Task.Run.
        return await Task.Run(() =>
        {
            using var pdfStream = File.OpenRead(pdfAbsolutePath);
            var opts = new PDFtoImage.RenderOptions { Dpi = 200 };
            // Use the Index-based overload (the int-page overload is marked obsolete in 4.x).
            PDFtoImage.Conversion.SavePng(outputAbsolutePath, pdfStream, page: (Index)0, options: opts);
            var info = Image.Identify(outputAbsolutePath);
            return (info.Width, info.Height);
        }, ct);
    }

    public async Task<(int WidthPx, int HeightPx)> GeneratePreviewAsync(
        string sourceAbsolutePath, string outputAbsolutePath,
        int rotationDegrees, int maxWidth, int quality, CancellationToken ct)
    {
        using var img = await Image.LoadAsync(sourceAbsolutePath, ct);

        var rot = BannerDimensions.NormalizeRotation(rotationDegrees);
        var rotateMode = rot switch
        {
            90  => RotateMode.Rotate90,
            180 => RotateMode.Rotate180,
            270 => RotateMode.Rotate270,
            _   => RotateMode.None
        };

        img.Mutate(ctx =>
        {
            if (rotateMode != RotateMode.None)
                ctx.Rotate(rotateMode);

            // Resize so the longer side is at most maxWidth, preserving aspect.
            var (w, h) = (ctx.GetCurrentSize().Width, ctx.GetCurrentSize().Height);
            var longer = Math.Max(w, h);
            if (longer > maxWidth)
            {
                var scale = (double)maxWidth / longer;
                var newW = (int)Math.Round(w * scale);
                var newH = (int)Math.Round(h * scale);
                ctx.Resize(newW, newH);
            }
        });

        var encoder = new JpegEncoder { Quality = quality };
        await img.SaveAsync(outputAbsolutePath, encoder, ct);

        return (img.Width, img.Height);
    }
}
