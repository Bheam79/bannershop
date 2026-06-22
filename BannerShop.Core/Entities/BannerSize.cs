namespace BannerShop.Core.Entities;

/// <summary>
/// A banner pricing rule. Each row defines a (material × width-range × height-range)
/// combination and how to compute the price for any banner whose actual dimensions
/// fall inside the range.
///
/// BANNERSH-255 — replaces the pre-existing "BannerSize per concrete size + optional
/// IsCustomWidth/IsCustomHeight" model with a range-based "rules table" so the admin
/// can encode banner-gluing pricing (1×, 2×, 3× …) directly:
///
/// Example rows for a 160 cm-wide material:
///   • MinW 1, MaxW 160, MinH 1,   MaxH 154, PricingHeight 154, Mult 1 — single panel
///   • MinW 1, MaxW 160, MinH 154, MaxH 300, PricingHeight 154, Mult 2 — 2-panel glue
///   • MinW 1, MaxW 160, MinH 300, MaxH 450, PricingHeight 154, Mult 3 — 3-panel glue
///   • MinW 267, MaxW 267, MinH 150, MaxH 150, FixedPrice 499 — standard 267×150 size
///
/// When multiple rules match a customer's requested (width, height, material) triple,
/// <see cref="BannerShop.Api.Services.PricingService"/> picks the cheapest. The
/// "isCustomWidth/isCustomHeight" flags and the old single WidthCm/HeightCm columns
/// are gone — every order now provides its actual dimensions.
/// </summary>
public class BannerSize
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int MaterialId { get; set; }
    public int SortOrder { get; set; }

    // ── Size range this rule applies to ──────────────────────────────────────
    /// <summary>Minimum banner width (cm) this rule covers (inclusive).</summary>
    public int MinWidthCm { get; set; }
    /// <summary>Maximum banner width (cm) this rule covers (inclusive).</summary>
    public int MaxWidthCm { get; set; }
    /// <summary>Minimum banner height (cm) this rule covers (inclusive).</summary>
    public int MinHeightCm { get; set; }
    /// <summary>Maximum banner height (cm) this rule covers (inclusive).</summary>
    public int MaxHeightCm { get; set; }

    // ── Pricing parameters ───────────────────────────────────────────────────
    /// <summary>
    /// Height (cm) plugged into the per-m² formula instead of the customer's
    /// actual height. Lets the admin charge "everything 1–154 cm tall is priced
    /// at 154 cm" so material waste is correctly billed.
    /// </summary>
    public int PricingHeightCm { get; set; }

    /// <summary>
    /// Multiplier applied to the formula price (typically 1, 2, or 3) so the
    /// admin can encode banner-gluing tiers without an external panel calc.
    /// Ignored when <see cref="FixedPrice"/> is set.
    /// </summary>
    public int PricingMultiplier { get; set; } = 1;

    /// <summary>
    /// When set, this rule charges a fixed price for any matching dimensions —
    /// the formula and the multiplier are skipped entirely. Used for printed
    /// standard sizes (e.g. 267×150 cm = 499 NOK).
    /// </summary>
    public decimal? FixedPrice { get; set; }

    // Navigation
    public Material Material { get; set; } = null!;
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
