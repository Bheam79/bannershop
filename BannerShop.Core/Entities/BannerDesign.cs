namespace BannerShop.Core.Entities;

/// <summary>
/// User-uploaded banner design (basic builder flow).
///
/// Stores the original file (image or PDF first page) plus a generated JPEG preview,
/// along with the rotation and user-selected height in cm. The computed width in cm
/// is derived from the (rotation-effective) aspect ratio and the selected height,
/// rounded to the nearest 10 cm.
///
/// Schema defined in BANNERSH-14 plan.
/// </summary>
public class BannerDesign
{
    public int Id { get; set; }
    public int UserId { get; set; }

    /// <summary>Original filename as uploaded by the user (display only).</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>Relative storage path for the (rasterized) original image, under the file-storage base path.</summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>MIME type of the stored original (after PDF conversion this is image/png).</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Original image width in pixels (before rotation).</summary>
    public int WidthPx { get; set; }

    /// <summary>Original image height in pixels (before rotation).</summary>
    public int HeightPx { get; set; }

    /// <summary>User-applied rotation in degrees: 0, 90, 180 or 270.</summary>
    public int RotationDegrees { get; set; }

    /// <summary>Customer-selected banner height in cm (typically 150 or 180).</summary>
    public int SelectedHeightCm { get; set; } = 150;

    /// <summary>Width in cm computed from rotation-effective aspect ratio and SelectedHeightCm, rounded to nearest 10.</summary>
    public int ComputedWidthCm { get; set; }

    /// <summary>Relative storage path for the JPEG preview (≤1200 px wide).</summary>
    public string? PreviewStoragePath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
}
