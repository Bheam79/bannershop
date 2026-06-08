using BannerShop.Core.Enums;
using BannerShop.Core.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests;

public class EyeletCalculatorTests
{
    // ── CountIntermediatesOnSegment ──────────────────────────────────────────

    [Theory]
    [InlineData(0,   0)]  // degenerate
    [InlineData(100, 0)]  // ≤ 120 → none
    [InlineData(120, 0)]  // exactly 120 → none
    [InlineData(121, 1)]  // just above 120 → centre
    [InlineData(200, 1)]  // 120 < 200 ≤ 260 → centre
    [InlineData(260, 1)]  // exactly 260 → centre
    [InlineData(261, 2)]  // just above 260 → 2 at 100cm from each end, recurse on 61 → 0
    [InlineData(300, 2)]  // 300: place at 100&200, recurse on 100 → 0
    [InlineData(400, 3)]  // 400: place at 100&300, recurse on 200 → 1
    [InlineData(500, 4)]  // 500: recurse: 300 → 2 → total 4
    [InlineData(600, 5)]  // 600: recurse: 400 → 3 → total 5
    [InlineData(700, 6)]  // 700: recurse: 500 → 4 → total 6
    public void CountIntermediatesOnSegment_ReturnsExpected(int length, int expected)
    {
        EyeletCalculator.CountIntermediatesOnSegment(length).Should().Be(expected);
    }

    // ── CountEyelets ─────────────────────────────────────────────────────────

    [Fact]
    public void CountEyelets_None_ReturnsZero()
    {
        EyeletCalculator.CountEyelets(300, 150, EyeletOption.None).Should().Be(0);
    }

    [Fact]
    public void CountEyelets_FourCorners_AlwaysReturnsFour()
    {
        EyeletCalculator.CountEyelets(100, 50,  EyeletOption.FourCorners).Should().Be(4);
        EyeletCalculator.CountEyelets(300, 150, EyeletOption.FourCorners).Should().Be(4);
        EyeletCalculator.CountEyelets(500, 180, EyeletOption.FourCorners).Should().Be(4);
    }

    [Theory]
    // width × height → expected total eyelets (4 corners + all intermediates)
    [InlineData(100, 50,  4)]  // both sides ≤ 120 → no intermediates → 4 corners
    [InlineData(300, 150, 10)] // width=300→2 each×2 sides=4; height=150→1 each×2 sides=2; +4
    [InlineData(400, 150, 12)] // width=400→3×2=6; height=150→1×2=2; +4=12
    [InlineData(300, 100, 8)]  // width=300→2×2=4; height=100→0×2=0; +4=8
    [InlineData(200, 200, 8)]  // width=200→1×2=2; height=200→1×2=2; +4=8
    public void CountEyelets_PerMeter_ReturnsExpected(int widthCm, int heightCm, int expected)
    {
        EyeletCalculator.CountEyelets(widthCm, heightCm, EyeletOption.PerMeter)
            .Should().Be(expected);
    }

    [Fact]
    public void CountEyelets_PerMeter_DegenerateZeroSize_ReturnsFour()
    {
        // Zero-size banner: we default to 0, not 4, since there's no banner to put eyelets on.
        EyeletCalculator.CountEyelets(0, 0, EyeletOption.PerMeter).Should().Be(0);
    }
}
