using BannerShop.Core.Enums;
using BannerShop.Core.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests;

public class EyeletPositionHelperTests
{
    // ── GetIntermediateCmPositions ────────────────────────────────────────────

    [Theory]
    [InlineData(100, 0)]   // ≤ 120 → none
    [InlineData(120, 0)]   // exactly 120 → none
    [InlineData(121, 1)]   // just above 120 → 1 centre
    [InlineData(200, 1)]
    [InlineData(260, 1)]
    [InlineData(261, 2)]   // 100 from each end, no recurse
    [InlineData(300, 2)]   // 100 & 200
    [InlineData(400, 3)]   // 100 & 300 + 1 in the 200-cm middle
    [InlineData(500, 4)]
    [InlineData(600, 5)]
    public void GetIntermediateCmPositions_CountMatchesCalculator(int sideLength, int expectedCount)
    {
        var positions = EyeletPositionHelper.GetIntermediateCmPositions(sideLength);
        positions.Count.Should().Be(expectedCount);
    }

    [Fact]
    public void GetIntermediateCmPositions_AreWithinSide()
    {
        // All positions must be strictly between 0 and sideLength.
        foreach (int side in new[] { 130, 270, 350, 450, 550 })
        {
            var positions = EyeletPositionHelper.GetIntermediateCmPositions(side);
            foreach (var p in positions)
            {
                p.Should().BeGreaterThan(0, because: $"side={side}");
                p.Should().BeLessThan(side, because: $"side={side}");
            }
        }
    }

    [Fact]
    public void GetIntermediateCmPositions_AreSorted()
    {
        var positions = EyeletPositionHelper.GetIntermediateCmPositions(600);
        positions.Should().BeInAscendingOrder();
    }

    // ── GetPixelPositions ─────────────────────────────────────────────────────

    [Fact]
    public void GetPixelPositions_None_ReturnsEmpty()
    {
        var positions = EyeletPositionHelper.GetPixelPositions(800, 400, 300, 150, EyeletOption.None);
        positions.Should().BeEmpty();
    }

    [Fact]
    public void GetPixelPositions_FourCorners_ReturnsFour()
    {
        var positions = EyeletPositionHelper.GetPixelPositions(800, 400, 300, 150, EyeletOption.FourCorners);
        positions.Count.Should().Be(4);
    }

    [Fact]
    public void GetPixelPositions_FourCorners_AreNearEdges()
    {
        // Each corner must be within the image bounds and near an edge (≤ 20 px from each border).
        const int W = 800, H = 400;
        var positions = EyeletPositionHelper.GetPixelPositions(W, H, 300, 150, EyeletOption.FourCorners);
        foreach (var (x, y) in positions)
        {
            x.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(W);
            y.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(H);

            // Each corner is close to one of the four edges.
            bool nearHorizontalEdge = x <= 20 || x >= W - 20;
            bool nearVerticalEdge   = y <= 20 || y >= H - 20;
            (nearHorizontalEdge || nearVerticalEdge).Should().BeTrue(
                because: $"corner ({x},{y}) should be near an edge in a {W}×{H} image");
        }
    }

    [Fact]
    public void GetPixelPositions_PerMeter_HasMoreThanFourPositions_ForWideBanner()
    {
        // A 300 cm wide banner should have intermediate eyelets along the top/bottom.
        var positions = EyeletPositionHelper.GetPixelPositions(800, 400, 300, 150, EyeletOption.PerMeter);
        // 4 corners + at least 2 top/bottom intermediates for a 300 cm width (> 260 cm)
        positions.Count.Should().BeGreaterThan(4);
    }

    [Fact]
    public void GetPixelPositions_PerMeter_AllWithinImageBounds()
    {
        const int W = 640, H = 320;
        var positions = EyeletPositionHelper.GetPixelPositions(W, H, 400, 150, EyeletOption.PerMeter);
        foreach (var (x, y) in positions)
        {
            x.Should().BeInRange(0, W - 1, because: $"position ({x},{y}) X is out of bounds");
            y.Should().BeInRange(0, H - 1, because: $"position ({x},{y}) Y is out of bounds");
        }
    }

    [Fact]
    public void GetPixelPositions_PerMeter_MatchesCalculatorCount()
    {
        // Total eyelet count from GetPixelPositions must match EyeletCalculator.CountEyelets.
        int widthCm = 300, heightCm = 150;
        int imgW = 800, imgH = 400;

        var positions = EyeletPositionHelper.GetPixelPositions(imgW, imgH, widthCm, heightCm, EyeletOption.PerMeter);
        int expected  = EyeletCalculator.CountEyelets(widthCm, heightCm, EyeletOption.PerMeter);

        positions.Count.Should().Be(expected);
    }
}
