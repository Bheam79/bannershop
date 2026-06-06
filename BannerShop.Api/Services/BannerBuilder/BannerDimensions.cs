namespace BannerShop.Api.Services.BannerBuilder;

/// <summary>
/// Pure, side-effect-free banner-dimension math.
/// Kept separate from the controller / file IO so it can be unit-tested in isolation.
/// </summary>
public static class BannerDimensions
{
    /// <summary>Minimum allowed width in cm for a printed banner.</summary>
    public const int MinWidthCm = 50;

    /// <summary>Maximum allowed width in cm for a printed banner.</summary>
    public const int MaxWidthCm = 1000;

    /// <summary>
    /// Returns the rotation-effective aspect ratio (W/H) given original pixel dimensions
    /// and a rotation in degrees (any multiple of 90). 90° and 270° swap W and H.
    /// </summary>
    public static double EffectiveAspectRatio(int widthPx, int heightPx, int rotationDegrees)
    {
        if (widthPx <= 0) throw new ArgumentOutOfRangeException(nameof(widthPx));
        if (heightPx <= 0) throw new ArgumentOutOfRangeException(nameof(heightPx));

        var rot = NormalizeRotation(rotationDegrees);
        return rot is 90 or 270
            ? (double)heightPx / widthPx
            : (double)widthPx / heightPx;
    }

    /// <summary>
    /// Computes the printed width in cm from the rotation-effective aspect ratio and the
    /// user-selected printed height in cm. The result is rounded to the nearest 10 cm and
    /// clamped to [MinWidthCm, MaxWidthCm].
    /// </summary>
    public static int ComputeWidthCm(int widthPx, int heightPx, int rotationDegrees, int selectedHeightCm)
    {
        var aspect = EffectiveAspectRatio(widthPx, heightPx, rotationDegrees);
        var rawCm = selectedHeightCm * aspect;
        var rounded = (int)Math.Round(rawCm / 10.0, MidpointRounding.AwayFromZero) * 10;
        return Math.Clamp(rounded, MinWidthCm, MaxWidthCm);
    }

    /// <summary>
    /// Normalizes any rotation value (positive or negative) into one of {0, 90, 180, 270}.
    /// </summary>
    public static int NormalizeRotation(int degrees)
    {
        var r = degrees % 360;
        if (r < 0) r += 360;
        // Snap to nearest quarter-turn (caller should only pass quarter-turns, but be defensive)
        return r switch
        {
            >= 0   and < 45  => 0,
            >= 45  and < 135 => 90,
            >= 135 and < 225 => 180,
            >= 225 and < 315 => 270,
            _ => 0
        };
    }
}
