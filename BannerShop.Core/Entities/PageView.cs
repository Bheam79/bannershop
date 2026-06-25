namespace BannerShop.Core.Entities;

/// <summary>
/// Records a single page-view event for visitor traffic analytics.
/// Append-only — one row per tracked page load, never updated.
/// </summary>
public class PageView
{
    public int Id { get; set; }

    /// <summary>Client-generated UUID, persisted in sessionStorage. A new UUID = a new session.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>True when this is the first page-view recorded for this SessionId.</summary>
    public bool IsNewSession { get; set; }

    /// <summary>URL path of the viewed page (e.g. "/", "/banner-builder/ai").</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Raw HTTP Referer header or client-side document.referrer (may be null/empty).</summary>
    public string? Referrer { get; set; }

    /// <summary>Classified referrer bucket: Direct | Google | Facebook | Instagram | Other.</summary>
    public string ReferrerSource { get; set; } = "Direct";

    /// <summary>IPv4 or IPv6 address of the requesting client (max 45 chars).</summary>
    public string? IpAddress { get; set; }

    /// <summary>Authenticated user — null for anonymous visitors.</summary>
    public int? UserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
