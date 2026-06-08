namespace BannerShop.Core.Entities;

/// <summary>
/// Records each anonymous (non-authenticated) AI generation attempt for rolling-30-day IP throttling.
/// Append-only — one row per generation, never updated.
/// </summary>
public class IpAiUsage
{
    public int Id { get; set; }

    /// <summary>IPv4 or IPv6 address of the requesting client (max 45 chars for IPv6).</summary>
    public string IpAddress { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
