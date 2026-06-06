using BannerShop.Api.Services.BannerBuilder;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Pure-math tests for the banner-builder dimension helper.
///
/// Two invariants must hold across every case:
///  1. Rotation 90°/270° swaps the effective W/H of the source.
///  2. Computed width is always rounded to the nearest 10 cm.
/// </summary>
public class BannerDimensionsTests
{
    // ── EffectiveAspectRatio ─────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 1920, 1080, 1.7777, 0.0001)]
    [InlineData(180, 1920, 1080, 1.7777, 0.0001)]
    [InlineData(90, 1920, 1080, 0.5625, 0.0001)]  // swaps to 1080/1920
    [InlineData(270, 1920, 1080, 0.5625, 0.0001)] // swaps to 1080/1920
    public void EffectiveAspectRatio_RespectsRotation(int rotation, int w, int h, double expected, double tolerance)
    {
        var actual = BannerDimensions.EffectiveAspectRatio(w, h, rotation);
        actual.Should().BeApproximately(expected, tolerance);
    }

    [Fact]
    public void EffectiveAspectRatio_RejectsZeroOrNegativeDimensions()
    {
        var ex1 = Record.Exception(() => BannerDimensions.EffectiveAspectRatio(0, 100, 0));
        var ex2 = Record.Exception(() => BannerDimensions.EffectiveAspectRatio(100, 0, 0));
        var ex3 = Record.Exception(() => BannerDimensions.EffectiveAspectRatio(-1, 100, 0));

        ex1.Should().BeOfType<ArgumentOutOfRangeException>();
        ex2.Should().BeOfType<ArgumentOutOfRangeException>();
        ex3.Should().BeOfType<ArgumentOutOfRangeException>();
    }

    // ── ComputeWidthCm ───────────────────────────────────────────────────────

    [Theory]
    // 16:9 landscape at 150 cm → 266.6 cm → rounded to 270
    [InlineData(1920, 1080, 0, 150, 270)]
    // 16:9 portrait (rotated 90°) at 150 cm → 84.4 cm → rounded to 80
    [InlineData(1920, 1080, 90, 150, 80)]
    // 16:9 landscape at 180 cm → 320 cm exactly
    [InlineData(1920, 1080, 0, 180, 320)]
    // Square at any height keeps height
    [InlineData(1000, 1000, 0, 150, 150)]
    [InlineData(1000, 1000, 90, 150, 150)]
    // 18:9 ultrawide at 150 → 300 cm
    [InlineData(2160, 1080, 0, 150, 300)]
    public void ComputeWidthCm_RoundsToNearest10cm(int wpx, int hpx, int rot, int heightCm, int expected)
    {
        var w = BannerDimensions.ComputeWidthCm(wpx, hpx, rot, heightCm);
        w.Should().Be(expected);
    }

    [Fact]
    public void ComputeWidthCm_ClampsToMinWidth()
    {
        // Very tall portrait image (1000x10000) at 150 cm height → 15 cm raw → clamped to MinWidthCm (50)
        var w = BannerDimensions.ComputeWidthCm(1000, 10000, 0, 150);
        w.Should().Be(BannerDimensions.MinWidthCm);
    }

    [Fact]
    public void ComputeWidthCm_ClampsToMaxWidth()
    {
        // Very wide image (20000x100) at 150 cm height → 30000 cm raw → clamped to MaxWidthCm (1000)
        var w = BannerDimensions.ComputeWidthCm(20000, 100, 0, 150);
        w.Should().Be(BannerDimensions.MaxWidthCm);
    }

    [Fact]
    public void ComputeWidthCm_RotationIsCommutativeForPairs()
    {
        // 0° and 180° should yield the same width; same for 90° and 270°.
        var a = BannerDimensions.ComputeWidthCm(1600, 900, 0, 150);
        var b = BannerDimensions.ComputeWidthCm(1600, 900, 180, 150);
        var c = BannerDimensions.ComputeWidthCm(1600, 900, 90, 150);
        var d = BannerDimensions.ComputeWidthCm(1600, 900, 270, 150);

        a.Should().Be(b);
        c.Should().Be(d);
    }

    // ── NormalizeRotation ────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 90)]
    [InlineData(180, 180)]
    [InlineData(270, 270)]
    [InlineData(360, 0)]
    [InlineData(450, 90)]    // 360 + 90
    [InlineData(-90, 270)]
    [InlineData(-180, 180)]
    [InlineData(720, 0)]
    public void NormalizeRotation_ProducesQuarterTurnInZeroTo270(int input, int expected)
    {
        BannerDimensions.NormalizeRotation(input).Should().Be(expected);
    }
}
