using BannerShop.Api.Services.DesignRequests;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests;

public class NoopUpscalingServiceTests
{
    [Fact]
    public async Task UpscaleAsync_ReturnsInputPathUnchanged()
    {
        var svc = new NoopUpscalingService();
        const string input = "/tmp/some-file.png";

        var result = await svc.UpscaleAsync(input);

        result.Should().Be(input);
    }

    [Fact]
    public async Task UpscaleAsync_DifferentScales_AlwaysReturnsInputPath()
    {
        var svc = new NoopUpscalingService();
        const string input = "/var/data/image.jpg";

        var r1 = await svc.UpscaleAsync(input, scale: 2);
        var r2 = await svc.UpscaleAsync(input, scale: 4);
        var r4 = await svc.UpscaleAsync(input, scale: 8);

        r1.Should().Be(input);
        r2.Should().Be(input);
        r4.Should().Be(input);
    }
}
