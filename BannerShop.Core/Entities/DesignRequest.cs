using BannerShop.Core.Enums;

namespace BannerShop.Core.Entities;

/// <summary>
/// A customer-placed design request — either AI-generated (95 kr) or human-designer (495 kr).
/// Treated as a mini-order with its own Stripe PaymentIntent (per BANNERSH-14 plan).
///
/// Schema and lifecycle are defined in BANNERSH-14, the entity is owned by this task
/// (BANNERSH-19) since BANNERSH-26 (consolidated foundation migration) is still TODO.
/// </summary>
public class DesignRequest
{
    public int Id { get; set; }

    /// <summary>
    /// Owner of the request — nullable since BANNERSH-67 introduced the free-first
    /// anonymous flow where a user can generate one banner without signing up.
    /// When null, <see cref="IpAddress"/> is the only identifier for the request.
    /// </summary>
    public int? UserId { get; set; }

    /// <summary>
    /// Client IP recorded for anonymous AI requests so the rolling-30-day throttle
    /// (BANNERSH-65) can be evaluated post-hoc. Always null for authenticated requests.
    /// </summary>
    public string? IpAddress { get; set; }

    public int BannerTemplateId { get; set; }

    /// <summary>AI vs. Manual flow.</summary>
    public DesignRequestMode Mode { get; set; }

    /// <summary>UI language for prompts and copy ("nb" or "en").</summary>
    public string Language { get; set; } = "nb";

    /// <summary>Name of the person the banner is for (e.g. the birthday celebrant).</summary>
    public string PersonName { get; set; } = string.Empty;

    /// <summary>Optional age (years) for the person — used in some prompt templates.</summary>
    public int? PersonAge { get; set; }

    /// <summary>The exact text that should appear on the banner.</summary>
    public string TextContent { get; set; } = string.Empty;

    /// <summary>Free-text theme / style hint (e.g. "tropisk fest, lilla og gull").</summary>
    public string ThemeDescription { get; set; } = string.Empty;

    /// <summary>Optional uploaded portrait — relative path under the file storage base path.</summary>
    public string? UploadedPhotoPath { get; set; }

    /// <summary>Customer-selected banner aspect ratio: "16:9" or "18:9" (= 2:1).</summary>
    public string AspectRatio { get; set; } = "16:9";

    /// <summary>Current lifecycle state.</summary>
    public DesignRequestStatus Status { get; set; } = DesignRequestStatus.Pending;

    /// <summary>Number of designer revisions completed so far (capped at 1 for Manual in service layer).</summary>
    public int RevisionCount { get; set; }

    /// <summary>How many free AI re-runs the customer still has on this paid request.</summary>
    public int RegenerationsRemaining { get; set; } = 1;

    /// <summary>Snapshotted price the customer paid (95 or 495 NOK).</summary>
    public decimal PriceNok { get; set; }

    /// <summary>Stripe PaymentIntent id (one per request — DesignRequests are mini-orders).</summary>
    public string? StripePaymentIntentId { get; set; }

    /// <summary>Relative storage path of the AI-generated PNG (uncropped 4K source).</summary>
    public string? AiResultStoragePath { get; set; }

    /// <summary>Relative storage path of the manually-uploaded designer preview (Manual mode).</summary>
    public string? DesignerPreviewPath { get; set; }

    /// <summary>Relative storage path of the cropped print-ready PNG.</summary>
    public string? FinalCroppedStoragePath { get; set; }

    /// <summary>Last error message recorded by the AI pipeline (if any).</summary>
    public string? LastError { get; set; }

    /// <summary>When the customer approved the preview (Manual flow).</summary>
    public DateTime? CustomerApprovedAt { get; set; }

    /// <summary>Internal notes from the designer / admin (Manual flow).</summary>
    public string? DesignerNotes { get; set; }

    /// <summary>
    /// FK to the <see cref="BannerDesign"/> row created when this request reaches Final status.
    /// Null until the design is finalised. The customer uses this id to add the print to their cart.
    /// </summary>
    public int? FinalBannerDesignId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ── BannerGeneration tracking (BANNERSH-66) ───────────────────────────────
    /// <summary>
    /// FK to the most-recently completed <see cref="BannerGeneration"/> row for this session.
    /// Null until the first generation finishes. Updated on each successful pipeline run.
    /// </summary>
    public int? CurrentGenerationId { get; set; }

    // Navigation
    public User? User { get; set; }
    public BannerTemplate BannerTemplate { get; set; } = null!;
    public BannerDesign? FinalBannerDesign { get; set; }
    public BannerGeneration? CurrentGeneration { get; set; }
    public ICollection<DesignRequestRevision> Revisions { get; set; } = new List<DesignRequestRevision>();
    public ICollection<BannerGeneration> Generations { get; set; } = new List<BannerGeneration>();
}
