using BannerShop.Api.Services.BannerBuilder;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Unit tests for <see cref="ImageProcessingService"/>. The class carries a class-level
/// <c>[ExcludeFromCodeCoverage]</c> ("tested via integration"), but its rotate/resize/crop math
/// had zero direct test coverage - every consumer (<c>BannerBuilderController</c>,
/// <c>DesignRequestService</c>, <c>AiGenerationPipeline</c>) only ever exercises a
/// <c>Mock&lt;IImageProcessingService&gt;</c>. Exercised against real ImageSharp-generated images
/// on a temp directory rather than mocked, since the whole point of the class is raster I/O.
/// PDF rendering (<see cref="ImageProcessingService.RenderPdfFirstPageToPngAsync"/>) is out of
/// scope - it needs a real PDFium-parseable fixture file, which isn't cheap to hand-construct.
/// </summary>
public class ImageProcessingServiceTests : IDisposable
{
    private readonly string _root;
    private readonly ImageProcessingService _service = new();

    public ImageProcessingServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bannershop-imgproc-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string MakeSourceImage(int width, int height, string fileName = "src.png")
    {
        var path = Path.Combine(_root, fileName);
        using var img = new Image<Rgba32>(width, height, Color.White);
        img.Save(path, new PngEncoder());
        return path;
    }

    private string OutPath(string fileName) => Path.Combine(_root, fileName);

    // -- ReadDimensionsAsync --------------------------------------------------

    [Fact]
    public async Task ReadDimensionsAsync_ReturnsActualPixelSize()
    {
        var src = MakeSourceImage(300, 150);

        var (w, h) = await _service.ReadDimensionsAsync(src, CancellationToken.None);

        w.Should().Be(300);
        h.Should().Be(150);
    }

    // -- GeneratePreviewAsync --------------------------------------------------

    [Fact]
    public async Task GeneratePreviewAsync_NoRotation_KeepsOrientation()
    {
        var src = MakeSourceImage(400, 200);
        var outPath = OutPath("preview.jpg");

        var (w, h) = await _service.GeneratePreviewAsync(src, outPath, rotationDegrees: 0, maxWidth: 1000, quality: 80, CancellationToken.None);

        w.Should().Be(400);
        h.Should().Be(200);
        File.Exists(outPath).Should().BeTrue();
        using var saved = await Image.LoadAsync(outPath);
        saved.Width.Should().Be(400);
        saved.Height.Should().Be(200);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(270)]
    public async Task GeneratePreviewAsync_QuarterTurnRotation_SwapsDimensions(int degrees)
    {
        var src = MakeSourceImage(400, 200);
        var outPath = OutPath("preview.jpg");

        var (w, h) = await _service.GeneratePreviewAsync(src, outPath, rotationDegrees: degrees, maxWidth: 1000, quality: 80, CancellationToken.None);

        w.Should().Be(200);
        h.Should().Be(400);
    }

    [Fact]
    public async Task GeneratePreviewAsync_180Rotation_KeepsDimensions()
    {
        var src = MakeSourceImage(400, 200);
        var outPath = OutPath("preview.jpg");

        var (w, h) = await _service.GeneratePreviewAsync(src, outPath, rotationDegrees: 180, maxWidth: 1000, quality: 80, CancellationToken.None);

        w.Should().Be(400);
        h.Should().Be(200);
    }

    [Fact]
    public async Task GeneratePreviewAsync_NonQuarterTurn_NormalizesToNoRotation()
    {
        // BannerDimensions.NormalizeRotation snaps anything in [-45,45) to 0.
        var src = MakeSourceImage(400, 200);
        var outPath = OutPath("preview.jpg");

        var (w, h) = await _service.GeneratePreviewAsync(src, outPath, rotationDegrees: 10, maxWidth: 1000, quality: 80, CancellationToken.None);

        w.Should().Be(400);
        h.Should().Be(200);
    }

    [Fact]
    public async Task GeneratePreviewAsync_LargerThanMaxWidth_DownscalesPreservingAspect()
    {
        var src = MakeSourceImage(2000, 1000);
        var outPath = OutPath("preview.jpg");

        var (w, h) = await _service.GeneratePreviewAsync(src, outPath, rotationDegrees: 0, maxWidth: 800, quality: 80, CancellationToken.None);

        w.Should().Be(800);
        h.Should().Be(400);
    }

    [Fact]
    public async Task GeneratePreviewAsync_SmallerThanMaxWidth_LeavesSizeUnchanged()
    {
        var src = MakeSourceImage(300, 150);
        var outPath = OutPath("preview.jpg");

        var (w, h) = await _service.GeneratePreviewAsync(src, outPath, rotationDegrees: 0, maxWidth: 800, quality: 80, CancellationToken.None);

        w.Should().Be(300);
        h.Should().Be(150);
    }

    [Fact]
    public async Task GeneratePreviewAsync_TallerImage_DownscalesByHeight()
    {
        var src = MakeSourceImage(1000, 2000);
        var outPath = OutPath("preview.jpg");

        var (w, h) = await _service.GeneratePreviewAsync(src, outPath, rotationDegrees: 0, maxWidth: 800, quality: 80, CancellationToken.None);

        h.Should().Be(800);
        w.Should().Be(400);
    }

    [Fact]
    public async Task GeneratePreviewAsync_RotatedThenOversized_ResizesAfterRotating()
    {
        // 2000x1000 rotated 90 degrees becomes an effective 1000x2000 image, which should then
        // be downscaled by its (now-longer) height, not the original width.
        var src = MakeSourceImage(2000, 1000);
        var outPath = OutPath("preview.jpg");

        var (w, h) = await _service.GeneratePreviewAsync(src, outPath, rotationDegrees: 90, maxWidth: 800, quality: 80, CancellationToken.None);

        h.Should().Be(800);
        w.Should().Be(400);
    }

    // -- CenterCropAsync --------------------------------------------------------

    [Fact]
    public async Task CenterCropAsync_SourceWiderThanTarget_CropsWidth()
    {
        // 400x200 source (ratio 2:1) cropped to 1:1 -> keep full height, crop width to 200.
        var src = MakeSourceImage(400, 200);
        var outPath = OutPath("crop.png");

        var (w, h) = await _service.CenterCropAsync(src, outPath, ratioWidth: 1, ratioHeight: 1, CancellationToken.None);

        w.Should().Be(200);
        h.Should().Be(200);
    }

    [Fact]
    public async Task CenterCropAsync_SourceTallerThanTarget_CropsHeight()
    {
        // 200x400 source (ratio 1:2) cropped to 1:1 -> keep full width, crop height to 200.
        var src = MakeSourceImage(200, 400);
        var outPath = OutPath("crop.png");

        var (w, h) = await _service.CenterCropAsync(src, outPath, ratioWidth: 1, ratioHeight: 1, CancellationToken.None);

        w.Should().Be(200);
        h.Should().Be(200);
    }

    [Fact]
    public async Task CenterCropAsync_SourceMatchesTargetRatio_NoEffectiveCrop()
    {
        var src = MakeSourceImage(400, 200);
        var outPath = OutPath("crop.png");

        var (w, h) = await _service.CenterCropAsync(src, outPath, ratioWidth: 2, ratioHeight: 1, CancellationToken.None);

        w.Should().Be(400);
        h.Should().Be(200);
    }

    [Fact]
    public async Task CenterCropAsync_WritesFileWithReturnedDimensions()
    {
        var src = MakeSourceImage(400, 200);
        var outPath = OutPath("crop.png");

        var (w, h) = await _service.CenterCropAsync(src, outPath, ratioWidth: 1, ratioHeight: 1, CancellationToken.None);

        File.Exists(outPath).Should().BeTrue();
        using var saved = await Image.LoadAsync(outPath);
        saved.Width.Should().Be(w);
        saved.Height.Should().Be(h);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public async Task CenterCropAsync_NonPositiveRatioComponent_Throws(int ratioWidth, int ratioHeight)
    {
        var src = MakeSourceImage(400, 200);
        var outPath = OutPath("crop.png");

        var act = () => _service.CenterCropAsync(src, outPath, ratioWidth, ratioHeight, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
