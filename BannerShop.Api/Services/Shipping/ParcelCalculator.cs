using BannerShop.Core.Entities;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Services.Shipping;

/// <summary>
/// Converts an ordered banner (size + optional custom width + qty) into the physical parcel
/// dimensions used by the carrier rating API.
///
/// Assumptions (all configurable via PricingParameter):
/// - Each banner is rolled into a tube whose length equals the banner width.
/// - The rolled tube diameter is approximately the same for every banner of typical material.
/// - Multiple banners are rolled together into one tube; the tube cross-section grows with √qty
///   (a reasonable approximation since rolled area scales linearly with qty).
/// - Packaging adds a fixed weight.
/// - Tube length is capped at the configured carrier maximum (banner is folded once beyond that).
/// </summary>
public class ParcelCalculator
{
    public const string KeyTubeDiameterCm   = "shipping_tube_diameter_cm";
    public const string KeyPackagingWeightG = "shipping_packaging_weight_g";
    public const string KeyMaxLengthCm      = "shipping_max_length_cm";

    private readonly BannerShopDbContext _db;

    public ParcelCalculator(BannerShopDbContext db) => _db = db;

    public async Task<ParcelDimensions> CalculateAsync(
        BannerSize size,
        int? customWidthCm,
        int qty,
        CancellationToken ct = default)
    {
        if (qty < 1) qty = 1;

        var p = await _db.PricingParameters
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Key, x => x.Value, ct);

        var tubeDiameter   = p.GetValueOrDefault(KeyTubeDiameterCm,   15m);
        var packagingGrams = p.GetValueOrDefault(KeyPackagingWeightG, 500m);
        var maxLength      = p.GetValueOrDefault(KeyMaxLengthCm,      240m);

        // ── Determine banner width in cm ─────────────────────────────────────
        int bannerWidthCm;
        if (size.IsCustomWidth)
        {
            // Fall back to the material's max width if the caller didn't pass one
            bannerWidthCm = customWidthCm
                ?? (size.Material?.WidthCm ?? 150);
        }
        else
        {
            bannerWidthCm = size.WidthCm
                ?? throw new InvalidOperationException($"Banner size {size.Id} has no WidthCm.");
        }

        // ── Tube length = banner width, capped at carrier maximum ────────────
        var tubeLengthCm = Math.Min((decimal)bannerWidthCm, maxLength);

        // ── Tube cross-section grows with √qty (rolled volume scales with qty) ─
        // Round up to whole cm for a slight safety margin.
        var crossSectionCm = (decimal)Math.Ceiling((double)tubeDiameter * Math.Sqrt(qty));

        // ── Weight: material gsm × area × qty + packaging ────────────────────
        var gsm = (decimal)(size.Material?.WeightGsm ?? 400);
        var areaSqm = (bannerWidthCm / 100m) * (size.HeightCm / 100m);
        var contentsGrams = gsm * areaSqm * qty;
        var totalGrams = contentsGrams + packagingGrams;
        var weightKg = decimal.Round(totalGrams / 1000m, 2);

        return new ParcelDimensions(
            LengthCm: tubeLengthCm,
            WidthCm: crossSectionCm,
            HeightCm: crossSectionCm,
            WeightKg: weightKg);
    }
}
