using BannerShop.Core.Enums;

namespace BannerShop.Core.Helpers;

/// <summary>
/// Pure (no I/O) helper that converts banner dimensions + eyelet option into
/// pixel coordinates for drawing eyelet circles on a preview image.
///
/// Mirrors the counting logic of <see cref="EyeletCalculator"/> but returns
/// actual (X, Y) positions rather than a count.
/// </summary>
public static class EyeletPositionHelper
{
    /// <summary>
    /// Returns pixel (cx, cy) centre positions for each eyelet to be drawn on a
    /// preview image of <paramref name="imgWidthPx"/> × <paramref name="imgHeightPx"/> pixels
    /// representing a banner of <paramref name="widthCm"/> × <paramref name="heightCm"/> cm.
    ///
    /// (0, 0) is the top-left pixel of the image.
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> GetPixelPositions(
        int imgWidthPx,
        int imgHeightPx,
        int widthCm,
        int heightCm,
        EyeletOption option)
    {
        if (option == EyeletOption.None || widthCm <= 0 || heightCm <= 0)
            return Array.Empty<(int, int)>();

        // Corner eyelets are placed with a small margin from the image border.
        // 15 px works well across both small thumbnails and large previews.
        const int Margin = 15;

        var positions = new List<(int, int)>
        {
            (Margin,                  Margin),                   // top-left
            (imgWidthPx  - Margin,    Margin),                   // top-right
            (imgWidthPx  - Margin,    imgHeightPx - Margin),     // bottom-right
            (Margin,                  imgHeightPx - Margin),     // bottom-left
        };

        if (option == EyeletOption.PerMeter)
        {
            // ── Intermediate eyelets along the top and bottom edges ──────────────
            int innerW = imgWidthPx - 2 * Margin;
            foreach (var cmPos in GetIntermediateCmPositions(widthCm))
            {
                int px = Margin + (int)Math.Round((double)cmPos / widthCm * innerW);
                positions.Add((px, Margin));                  // top
                positions.Add((px, imgHeightPx - Margin));    // bottom
            }

            // ── Intermediate eyelets along the left and right edges ──────────────
            int innerH = imgHeightPx - 2 * Margin;
            foreach (var cmPos in GetIntermediateCmPositions(heightCm))
            {
                int py = Margin + (int)Math.Round((double)cmPos / heightCm * innerH);
                positions.Add((Margin, py));                  // left
                positions.Add((imgWidthPx - Margin, py));     // right
            }
        }

        return positions;
    }

    /// <summary>
    /// Returns the cm offsets of intermediate eyelets between two corner eyelets
    /// on a side of length <paramref name="sideLength"/> cm.
    /// The returned positions are in the open interval (0, sideLength).
    /// </summary>
    public static IReadOnlyList<int> GetIntermediateCmPositions(int sideLength)
    {
        var result = new List<int>();
        AddIntermediate(result, 0, sideLength);
        result.Sort();
        return result;
    }

    // Mirrors EyeletCalculator.CountIntermediatesOnSegment but collects positions.
    private static void AddIntermediate(List<int> result, int start, int end)
    {
        int length = end - start;
        if (length <= 120) return;
        if (length <= 260)
        {
            result.Add((start + end) / 2);
            return;
        }
        // Two eyelets at 100 cm from each end of this segment.
        int left  = start + 100;
        int right = end   - 100;
        result.Add(left);
        result.Add(right);
        // Recurse on the middle sub-segment.
        AddIntermediate(result, left, right);
    }
}
