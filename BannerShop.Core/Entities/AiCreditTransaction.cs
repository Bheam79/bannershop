using BannerShop.Core.Enums;

namespace BannerShop.Core.Entities;

/// <summary>
/// Immutable ledger row for every AI credit grant or consume operation.
/// Positive <see cref="Amount"/> = credits added; negative = credits consumed.
/// </summary>
public class AiCreditTransaction
{
    public int Id { get; set; }

    /// <summary>Null for anonymous (IP-only) grants/consumes.</summary>
    public int? UserId { get; set; }

    /// <summary>IP address — set for anonymous operations; may also be set for authenticated ones for audit.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Positive = grant, negative = consume.</summary>
    public int Amount { get; set; }

    public CreditReason Reason { get; set; }

    /// <summary>
    /// Deduplication key — Stripe PaymentIntent id for pack purchases,
    /// "order:{orderId}" for banner-order activations. Null for free / consume rows.
    /// </summary>
    public string? ReferenceId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User? User { get; set; }
}
