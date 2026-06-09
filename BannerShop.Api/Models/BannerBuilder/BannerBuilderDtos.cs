using System.ComponentModel.DataAnnotations;

namespace BannerShop.Api.Models.BannerBuilder;

/// <summary>Response from POST /api/banner-builder/upload.</summary>
public sealed class UploadResponseDto
{
    public int DesignId { get; set; }
    public string PreviewUrl { get; set; } = string.Empty;
    public int WidthPx { get; set; }
    public int HeightPx { get; set; }
    public int SelectedHeightCm { get; set; }
    public int ComputedWidthCm { get; set; }
    public int RotationDegrees { get; set; }
}

/// <summary>Request body for PUT /api/banner-builder/{id}/rotate.</summary>
public sealed class RotateRequestDto
{
    /// <summary>Rotation delta (added to current rotation, then normalized mod 360). Valid values: 90, 180, 270.</summary>
    [Range(-360, 360)]
    public int Degrees { get; set; }
}

/// <summary>Response from PUT /api/banner-builder/{id}/rotate.</summary>
public sealed class RotateResponseDto
{
    public string PreviewUrl { get; set; } = string.Empty;
    public int RotationDegrees { get; set; }
    public int ComputedWidthCm { get; set; }
    public int ComputedHeightCm { get; set; }
}

/// <summary>Request body for PUT /api/banner-builder/{id}/height.</summary>
public sealed class HeightRequestDto
{
    /// <summary>Customer-selected banner height in cm (typically 150 or 180).</summary>
    [Range(50, 1000)]
    public int HeightCm { get; set; }
}

/// <summary>Response from PUT /api/banner-builder/{id}/height.</summary>
public sealed class HeightResponseDto
{
    public int SelectedHeightCm { get; set; }
    public int ComputedWidthCm { get; set; }
}

/// <summary>One item in the GET /api/banner-builder/mine list.</summary>
public sealed class UploadedDesignListItemDto
{
    public int Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public int SelectedHeightCm { get; set; }
    public int ComputedWidthCm { get; set; }
    /// <summary>Public URL for the JPEG preview thumbnail.</summary>
    public string? PreviewUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}
