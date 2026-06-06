using System.ComponentModel.DataAnnotations;

namespace BannerShop.Api.Models.DesignRequests;

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
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
