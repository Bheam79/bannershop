using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.Shipping;
using BannerShop.Api.Services.Orders.Stripe;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace BannerShop.Tests.Helpers;

/// <summary>
/// Extended test factory that registers a mock IImageProcessingService
/// so BannerBuilderController tests can run without real image files.
/// </summary>
public class BannerBuilderTestFactory : TestWebApplicationFactory
{
    public Mock<IImageProcessingService> ImageProcessingMock { get; } = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

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
}
