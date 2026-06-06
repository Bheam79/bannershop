namespace BannerShop.Core.Entities;

/// <summary>
/// One revision-cycle log row for a <see cref="DesignRequest"/>.
/// Each row captures the customer comment that triggered a designer rework.
/// </summary>
public class DesignRequestRevision
{
    public int Id { get; set; }

    public int DesignRequestId { get; set; }

    /// <summary>1-based ordinal of this revision within the parent request.</summary>
    public int RevisionNumber { get; set; }

    /// <summary>What the customer said they wanted changed.</summary>
    public string CustomerComment { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public DesignRequest DesignRequest { get; set; } = null!;
}
