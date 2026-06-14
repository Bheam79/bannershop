using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.Shipping;
using BannerShop.Api.Services.Orders.Stripe;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace BannerShop.Tests.Helpers;

/// <summary>
/// Extended test factory that registers a mock IImageProcessingService
/// so BannerBuilderController tests can run without real image files.
///
/// Also overrides FileStorage:LocalRoot to a per-run temp directory so the
/// upload endpoint can write real files during tests without polluting the
/// shared /workspace/uploads location. The temp directory is removed in
/// Dispose so each test run starts clean.
/// </summary>
public class BannerBuilderTestFactory : TestWebApplicationFactory
{
    public Mock<IImageProcessingService> ImageProcessingMock { get; } = new();

    private readonly string _tempRoot =
        Path.Combine(Path.GetTempPath(), "bb-test-" + Guid.NewGuid().ToString("N"));

    /// <summary>Filesystem root that FileStorage:LocalRoot is overridden to. Exposed for tests
    /// that need to write real files (e.g. to exercise GetPreview's File.Exists check).</summary>
    public string TempRoot => _tempRoot;

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:LocalRoot"]     = _tempRoot,
                ["FileStorage:PublicBaseUrl"] = "/files",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace the real ImageProcessingService with a mock
            var imgDescriptors = services
                .Where(d => d.ServiceType == typeof(IImageProcessingService))
                .ToList();
            foreach (var d in imgDescriptors)
                services.Remove(d);

            // Default mock returns fake 1920x1080 dimensions
            ImageProcessingMock
                .Setup(s => s.ReadDimensionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((1920, 1080));

            ImageProcessingMock
                .Setup(s => s.GeneratePreviewAsync(
                    It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((640, 360));

            ImageProcessingMock
                .Setup(s => s.CenterCropAsync(
                    It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int>(), It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((1920, 1080));

            services.AddSingleton<IImageProcessingService>(ImageProcessingMock.Object);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best effort — temp dir cleanup must not fail tests */ }
        }
    }
}
