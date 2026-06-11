using BannerShop.Core.Enums;

namespace BannerShop.Core.Entities;

/// <summary>
/// Immutable record of a single AI generation attempt within a <see cref="DesignRequest"/> session.
/// One row per attempt; the currently-shown result has <see cref="IsActive"/> = true.
/// Created either by the <c>POST /design-requests/{id}/regenerate</c> endpoint (then updated by
/// the pipeline) or directly by the pipeline on the initial free generation.
/// </summary>
public class BannerGeneration
{
    public int Id { get; set; }

    public int DesignRequestId { get; set; }

    /// <summary>Relative storage path of the raw AI-generated PNG (uncropped 4K source).</summary>
    public string? StoragePath { get; set; }

    /// <summary>Relative storage path of the cropped print-ready PNG (same as StoragePath for non-18:9).</summary>
    public string? CroppedStoragePath { get; set; }

    /// <summary>
    /// Relative storage path of the low-resolution JPEG preview for this generation attempt
    /// (max 640 px on the longer side — BANNERSH-91 low-res preview pattern).
    /// Populated by the pipeline alongside CroppedStoragePath. Used for thumbnail display
    /// in the wizard's generation history strip so customers can switch between versions.
    /// Null for generations created before this field was added.
    /// </summary>
    public string? PreviewPath { get; set; }

    public BannerGenerationStatus Status { get; set; } = BannerGenerationStatus.Pending;

    /// <summary>Last error message if <see cref="Status"/> is <see cref="BannerGenerationStatus.Failed"/>.</summary>
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Set when the pipeline finishes (successfully or not).</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// True for the generation currently shown to the customer.
    /// At most one row per DesignRequest has IsActive = true.
    /// </summary>
    public bool IsActive { get; set; } = false;

    // Navigation
    public DesignRequest DesignRequest { get; set; } = null!;
}
