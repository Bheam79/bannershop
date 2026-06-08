using BannerShop.Core.Enums;

namespace BannerShop.Core.Helpers;

/// <summary>
/// Pure (no I/O) helper for calculating the number of eyelets (maljer) on a banner.
/// Hem/sewing is not possible on PVC banners; only eyelet options are offered.
/// </summary>
public static class EyeletCalculator
{
    /// <summary>
    /// Count the total number of eyelets for a banner of the given dimensions.
    /// </summary>
    public static int CountEyelets(int widthCm, int heightCm, EyeletOption option) => option switch
    {
        EyeletOption.None        => 0,
        EyeletOption.FourCorners => 4,
        EyeletOption.PerMeter    => CountPerMeter(widthCm, heightCm),
        _                        => 0
    };

    private static int CountPerMeter(int widthCm, int heightCm)
    {
        if (widthCm <= 0 || heightCm <= 0) return 0;
        // 4 fixed corner eyelets + intermediate eyelets on each of the 4 sides.
        var intermediates =
            CountIntermediatesOnSegment(widthCm)  // top side
          + CountIntermediatesOnSegment(widthCm)  // bottom side
          + CountIntermediatesOnSegment(heightCm) // left side
          + CountIntermediatesOnSegment(heightCm);// right side
        return 4 + intermediates;
    }

    /// <summary>
    /// Count the number of intermediate eyelets placed between two corner eyelets
    /// that are <paramref name="sideLength"/> cm apart, using the iterative spacing rule:
    /// <list type="bullet">
    ///   <item>distance ≤ 120 cm → no intermediates needed</item>
    ///   <item>120 &lt; distance ≤ 260 cm → 1 eyelet in the centre</item>
    ///   <item>distance &gt; 260 cm → 2 eyelets placed 100 cm from each corner end,
    ///         then recurse on the remaining middle segment</item>
    /// </list>
    /// </summary>
    public static int CountIntermediatesOnSegment(int sideLength)
    {
        if (sideLength <= 120) return 0;
        if (sideLength <= 260) return 1;
        // Place one 100 cm from each corner (2 new eyelets), then recurse on the
        // middle segment whose length = sideLength − 2 × 100.
        return 2 + CountIntermediatesOnSegment(sideLength - 200);
    }
}
