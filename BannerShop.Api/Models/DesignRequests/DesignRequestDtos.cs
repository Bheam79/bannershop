using System.ComponentModel.DataAnnotations;

namespace BannerShop.Api.Models.DesignRequests;

/// <summary>POST /api/design-requests/manual body.</summary>
public class CreateManualDesignRequestDto
{
    [Required]
    public int TemplateId { get; set; }

    [Required]
    [RegularExpression("^(nb|en)$", ErrorMessage = "Language must be 'nb' or 'en'.")]
    public string Language { get; set; } = "nb";

    [Required, StringLength(200, MinimumLength = 1)]
    public string PersonName { get; set; } = string.Empty;

    [Range(0, 130)]
    public int? PersonAge { get; set; }

    [Required, StringLength(500, MinimumLength = 1)]
    public string TextContent { get; set; } = string.Empty;

    [Required, StringLength(500)]
    public string ThemeDescription { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(16:9|18:9)$", ErrorMessage = "AspectRatio must be '16:9' or '18:9'.")]
    public string AspectRatio { get; set; } = "16:9";

    /// <summary>Optional BannerDesign id for an uploaded portrait photo.</summary>
    public int? UploadedPhotoBannerDesignId { get; set; }
}

/// <summary>POST /api/design-requests/{id}/revision body.</summary>
public class RequestRevisionDto
{
    [Required, StringLength(2000, MinimumLength = 1)]
    public string Comment { get; set; } = string.Empty;
}

/// <summary>POST /api/design-requests/ai body.</summary>
public class CreateAiDesignRequestDto
{
    [Required]
    public int TemplateId { get; set; }

    [Required]
    [RegularExpression("^(nb|en)$", ErrorMessage = "Language must be 'nb' or 'en'.")]
    public string Language { get; set; } = "nb";

    [Required, StringLength(200, MinimumLength = 1)]
    public string PersonName { get; set; } = string.Empty;

    [Range(0, 130)]
    public int? PersonAge { get; set; }

    [Required, StringLength(500, MinimumLength = 1)]
    public string TextContent { get; set; } = string.Empty;

    [Required, StringLength(500)]
    public string ThemeDescription { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(16:9|18:9)$", ErrorMessage = "AspectRatio must be '16:9' or '18:9'.")]
    public string AspectRatio { get; set; } = "16:9";

    /// <summary>
    /// Optional ID of a previously-uploaded portrait. Wired in via the existing
    /// BannerDesign upload endpoint (BANNERSH-15) — the rasterised image acts as
    /// the reference for gpt-image-2's edit endpoint.
    /// </summary>
    public int? UploadedPhotoBannerDesignId { get; set; }
}

/// <summary>Response from POST /api/design-requests/ai (and /manual once that lands).</summary>
public class CreateDesignRequestResponseDto
{
    public int DesignRequestId { get; set; }
    public string ClientSecret { get; set; } = string.Empty;
    public decimal TotalNok { get; set; }
}

/// <summary>Response from POST /api/design-requests/ai (BANNERSH-67 free-first flow).</summary>
public class CreateAiDesignRequestResponseDto
{
    public int DesignRequestId { get; set; }

    /// <summary>
    /// Anonymous callers receive this hint so the frontend can prompt the user to
    /// sign up before they hit /approve (which requires auth).
    /// </summary>
    public bool RequiresAuth { get; set; }

    /// <summary>Remaining credits after this generation (auth path only — 0 for anonymous).</summary>
    public int CreditsRemaining { get; set; }
}

/// <summary>
/// 402 paywall response body for POST /api/design-requests/ai (BANNERSH-67).
/// Returned when the caller has exhausted their free generation and has no credits.
/// </summary>
public class AiPaywallResponseDto
{
    /// <summary>'ip_limit_reached' | 'insufficient_credits'.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Credit balance for authenticated callers (always 0 for anonymous).</summary>
    public int CreditsRemaining { get; set; }

    public PaywallOptions PaywallOptions { get; set; } = new();
}

/// <summary>The set of next-step prompts the frontend can offer when the user hits the paywall.</summary>
public class PaywallOptions
{
    public decimal CreditPackPriceNok { get; set; }
    public int CreditPackCount { get; set; }
    public decimal BannerOrderActivationFeeNok { get; set; }
    public string ManualDesignerUrl { get; set; } = "/banner-builder/manual";
    public string UploadOwnUrl { get; set; } = "/banner-builder";
}

/// <summary>List item for GET /api/design-requests.</summary>
public class DesignRequestListItemDto
{
    public int Id { get; set; }
    public int BannerTemplateId { get; set; }
    public string Mode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = string.Empty;
    public decimal PriceNok { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── BANNERSH-83: extra fields so the AI banner wizard can render past generations
    //                as cards with a thumbnail + caption without an extra detail fetch.
    /// <summary>Customer-visible preview URL of the currently active (or final) result, or null if not generated yet.</summary>
    public string? PreviewUrl { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public string ThemeDescription { get; set; } = string.Empty;
}

/// <summary>Detail for GET /api/design-requests/{id}.</summary>
public class DesignRequestDetailDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int BannerTemplateId { get; set; }
    public string Mode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public int? PersonAge { get; set; }
    public string TextContent { get; set; } = string.Empty;
    public string ThemeDescription { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = string.Empty;
    public int RevisionCount { get; set; }
    public int RegenerationsRemaining { get; set; }
    public decimal PriceNok { get; set; }
    public string? StripePaymentIntentId { get; set; }
    public string? PreviewUrl { get; set; }
    public string? FinalCroppedUrl { get; set; }
    public int? FinalBannerDesignId { get; set; }
    public int? CurrentGenerationId { get; set; }
    public string? LastError { get; set; }
    public DateTime? CustomerApprovedAt { get; set; }
    public string? DesignerNotes { get; set; }
    public IReadOnlyList<DesignRequestRevisionDto> Revisions { get; set; } = Array.Empty<DesignRequestRevisionDto>();
    /// <summary>All generation attempts, oldest first. Image URLs are not included — only the active one is shown via PreviewUrl.</summary>
    public IReadOnlyList<BannerGenerationHistoryItemDto> GenerationHistory { get; set; } = Array.Empty<BannerGenerationHistoryItemDto>();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>One entry in the generation history list (no image URLs — only active one is surfaced).</summary>
public class BannerGenerationHistoryItemDto
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>POST /api/design-requests/{id}/regenerate body.</summary>
public class RegenerateAiRequestDto
{
    /// <summary>Optional updated banner text. If omitted, the existing value is kept.</summary>
    public string? TextContent { get; set; }

    /// <summary>Optional updated theme/style description. If omitted, the existing value is kept.</summary>
    public string? ThemeDescription { get; set; }
}

/// <summary>202 response from POST /api/design-requests/{id}/regenerate.</summary>
public class RegenerateAiResponseDto
{
    public int GenerationId { get; set; }
    public int CreditsRemaining { get; set; }
}

/// <summary>Snapshot of a single revision comment.</summary>
public class DesignRequestRevisionDto
{
    public int Id { get; set; }
    public int RevisionNumber { get; set; }
    public string CustomerComment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

// ── Admin DTOs ────────────────────────────────────────────────────────────────

/// <summary>Query filters for GET /api/admin/design-requests.</summary>
public class AdminDesignRequestFilter
{
    public string? Status { get; set; }
    public string? Mode { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>Paginated list response for the admin endpoint.</summary>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}

/// <summary>Admin list item — includes customer info.</summary>
public class AdminDesignRequestListItemDto
{
    public int Id { get; set; }
    public string Mode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = string.Empty;
    public decimal PriceNok { get; set; }
    public int BannerTemplateId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public int? PersonAge { get; set; }
    // Customer info
    public int UserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public int RevisionCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Full admin detail — all fields including upload paths and revision history.</summary>
public class AdminDesignRequestDetailDto : DesignRequestDetailDto
{
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string? UploadedPhotoUrl { get; set; }
    public string? TemplateName { get; set; }
}

/// <summary>PUT /api/admin/design-requests/{id}/status body.</summary>
public class AdminUpdateStatusDto
{
    [Required]
    public string Status { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Notes { get; set; }
}
