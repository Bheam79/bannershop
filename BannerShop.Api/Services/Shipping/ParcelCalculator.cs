using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Services.Shipping;

/// <summary>
/// Converts an ordered banner (material + actual dimensions + qty + packing mode)
/// into the physical parcel dimensions used by the carrier rating API.
///
/// BANNERSH-143 — two customer-selectable packing modes:
///
/// <list type="bullet">
/// <item><description>
/// <b>Rolled</b> (default): banner is wound into a tube whose length equals the
/// shorter side + 2 cm. The tube cross-section starts at 9 × 9 cm and grows by
/// 0.5 cm per metre of the long side. Multiple rolled banners share one tube
/// whose cross-section scales by √qty (volume scales linearly with qty).
/// </description></item>
/// <item><description>
/// <b>Folded</b>: banner is folded into a flat parcel 50 × 60 cm. Height is
/// 10 cm + 1 cm per metre of the long side. Multiple folded banners stack —
/// height multiplies by qty.
/// </description></item>
/// </list>
///
/// Weight stays material-area × gsm × qty + packaging in both modes.
///
/// BANNERSH-255: the old <see cref="BannerSize"/>-driven overloads are gone — the
/// banner-size catalogue is now a range-based pricing-rules table, so callers must
/// pass the concrete (widthCm, heightCm, materialWeightGsm) directly.
/// </summary>
public class ParcelCalculator
{
    public const string KeyPackagingWeightG = "shipping_packaging_weight_g";

    // ── Rolled-tube geometry (BANNERSH-143) ───────────────────────────────────
    private const decimal RolledLengthPaddingCm   = 2m;   // tube is shortest-side + 2 cm
    private const decimal RolledBaseCrossCm       = 9m;   // 9 × 9 cm tube cross-section base
    private const decimal RolledCrossPerMeterCm   = 0.5m; // + 0.5 cm per metre of long side

    // ── Folded-parcel geometry (BANNERSH-143) ─────────────────────────────────
    private const decimal FoldedWidthCm           = 50m;  // flat parcel: 50 × 60 cm footprint
    private const decimal FoldedLengthCm          = 60m;
    private const decimal FoldedBaseHeightCm      = 10m;
    private const decimal FoldedHeightPerMeterCm  = 1m;   // + 1 cm per metre of long side

    private readonly BannerShopDbContext _db;

    public ParcelCalculator(BannerShopDbContext db) => _db = db;

    public Task<ParcelDimensions> CalculateAsync(
        int widthCm,
        int heightCm,
        int materialWeightGsm,
        int qty,
        CancellationToken ct = default)
        => CalculateAsync(widthCm, heightCm, materialWeightGsm, qty, PackingMode.Rolled, ct);

    public async Task<ParcelDimensions> CalculateAsync(
        int widthCm,
        int heightCm,
        int materialWeightGsm,
        int qty,
        PackingMode packing,
        CancellationToken ct = default)
    {
        if (qty < 1) qty = 1;
        if (widthCm <= 0) throw new InvalidOperationException("Banner width must be > 0.");
        if (heightCm <= 0) throw new InvalidOperationException("Banner height must be > 0.");

        // BANNERSH-180: fallback packaging weight reduced from 500 g → 200 g
        // (Michael's measured average). Existing DBs keep whatever the admin
        // has stored in `shipping_packaging_weight_g` — editable in /admin/pricing.
        var packagingGrams = await _db.PricingParameters
            .AsNoTracking()
            .Where(x => x.Key == KeyPackagingWeightG)
            .Select(x => (decimal?)x.Value)
            .FirstOrDefaultAsync(ct) ?? 200m;

        // For these formulas "long side" is whichever banner edge is wider, and
        // "shortest side" is the other. For a 300×150 banner the long side is
        // 300 cm and the shortest is 150 cm.
        var longestCm  = Math.Max(widthCm, heightCm);
        var shortestCm = Math.Min(widthCm, heightCm);
        var longestM   = longestCm / 100m;

        decimal lengthCm, parcelWidthCm, heightOutCm;

        if (packing == PackingMode.Folded)
        {
            // Folded: fixed 50×60 cm footprint, height scales with long side and qty.
            var perItemHeight = FoldedBaseHeightCm + FoldedHeightPerMeterCm * longestM;
            lengthCm      = FoldedLengthCm;
            parcelWidthCm = FoldedWidthCm;
            heightOutCm   = decimal.Round(perItemHeight * qty, 1);
        }
        else
        {
            // Rolled: tube length = shortest + 2 cm; cross-section scales with √qty.
            var perItemCross = RolledBaseCrossCm + RolledCrossPerMeterCm * longestM;
            var crossSection = decimal.Round(perItemCross * (decimal)Math.Sqrt(qty), 1);
            lengthCm      = shortestCm + RolledLengthPaddingCm;
            parcelWidthCm = crossSection;
            heightOutCm   = crossSection;
        }

        // ── Weight: material gsm × area × qty + packaging (mode-independent) ─
        var gsm = (decimal)materialWeightGsm;
        var areaSqm = (widthCm / 100m) * (heightCm / 100m);
        var contentsGrams = gsm * areaSqm * qty;
        var totalGrams = contentsGrams + packagingGrams;
        var weightKg = decimal.Round(totalGrams / 1000m, 2);

        // Rolled banners ship in a cylindrical tube.
        // NonStackable = true: a tube cannot be stacked on other packages (Bring API flag).
        // VolumeSpecial = true: per Bring Shipping Guide 2.0 docs this flag is specifically
        //   "typically used for roll or other weirdly shaped packages" — i.e. the quoted
        //   price will be adjusted for special volume.  This is the primary flag that
        //   triggers Bring's tube/rørpakke pricing surcharge (BANNERSH-242).
        var nonStackable  = packing == PackingMode.Rolled;
        var volumeSpecial = packing == PackingMode.Rolled;

        return new ParcelDimensions(
            LengthCm: lengthCm,
            WidthCm: parcelWidthCm,
            HeightCm: heightOutCm,
            WeightKg: weightKg,
            NonStackable: nonStackable,
            VolumeSpecial: volumeSpecial);
    }
}
