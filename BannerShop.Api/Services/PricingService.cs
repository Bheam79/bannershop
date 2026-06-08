using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Core.Helpers;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Services;

public class PricingService : IPricingService
{
    private readonly BannerShopDbContext _db;

    public PricingService(BannerShopDbContext db) => _db = db;

    public async Task<decimal> CalculatePriceAsync(BannerSize size, int? customWidthCm = null)
    {
        // Fixed-price sizes (e.g. 300×180 cm at 699 NOK) skip the formula AND the panel
        // multiplier — admins set those manually and are expected to bake any gluing
        // surcharge into the fixed price.
        if (size.FixedPrice.HasValue)
            return size.FixedPrice.Value;

        // Load pricing parameters as a dictionary
        var p = await _db.PricingParameters
            .ToDictionaryAsync(x => x.Key, x => x.Value);

        // Use the material-specific price per m² when the Material navigation is loaded;
        // fall back to the global pricing parameter as a safety net for callers that
        // forget to .Include(s => s.Material) — prevents accidental pricing crashes.
        var basePricePerSqm = size.Material?.PricePerSqm
            ?? p.GetValueOrDefault("base_price_per_sqm", 180m);
        var minimumPrice    = p.GetValueOrDefault("minimum_price",        399m);
        var customSurcharge = p.GetValueOrDefault("custom_width_surcharge", 150m);
        // NOTE: hem (søm) is not possible on PVC banners; eyelets (maljer) are a separate
        // per-eyelet addon calculated via CalculateEyeletCostAsync — not included here.
        // BANNERSH-88: overlap between adjacent panels when a wide banner must be glued
        // together from multiple lengths. Used to determine the panel-count multiplier.
        var panelOverlapCm  = (int)p.GetValueOrDefault("banner_panel_overlap_cm", 5m);

        // Determine the effective width
        int widthCm;
        if (size.IsCustomWidth)
        {
            // Use caller-supplied width; fall back to minimum price width estimate
            widthCm = customWidthCm ?? 0;
        }
        else
        {
            widthCm = size.WidthCm
                ?? throw new InvalidOperationException($"Banner size {size.Id} has no WidthCm.");
        }

        decimal basePrice;
        if (widthCm <= 0)
        {
            // No width provided for a custom-width size → return minimum price + surcharges
            basePrice = minimumPrice;
        }
        else
        {
            var areaSqm = (widthCm / 100m) * (size.HeightCm / 100m);
            basePrice = Math.Max(minimumPrice, areaSqm * basePricePerSqm);
        }

        var surcharge = size.IsCustomWidth ? customSurcharge : 0m;
        var pricePerPanel = basePrice + surcharge;

        // BANNERSH-88: panel-count multiplier. If the banner is wider than the material
        // can produce as a single piece (Material.MaxBannerWidthCm), the price scales by
        // the number of panels needed:
        //   1 panel  : width ≤ M
        //   2 panels : M < width ≤ 2M − overlap
        //   3 panels : 2M − overlap < width ≤ 3M − 2·overlap
        //   n panels : (n−1)M − (n−2)·overlap < width ≤ nM − (n−1)·overlap
        // Each adjacent pair of panels overlaps by at least `panelOverlapCm` so the seam
        // is double-printed and can be welded/glued. The material navigation property may
        // be null when callers forget to .Include(s => s.Material) — in that case we skip
        // the multiplier rather than crash (single-panel pricing, same as before).
        var maxWidthPerPanel = ResolveMaxBannerWidthCm(size.Material);
        var panels = PanelsNeeded(widthCm, maxWidthPerPanel, panelOverlapCm);
        return pricePerPanel * panels;
    }

    /// <summary>
    /// Returns the effective max-banner-width for a material. Falls back to
    /// <see cref="Material.WidthCm"/> when the explicit field is 0/unset, and to a very
    /// large sentinel when the navigation property is missing (so we don't accidentally
    /// trigger the multiplier on uninitialised data).
    /// </summary>
    private static int ResolveMaxBannerWidthCm(Material? material)
    {
        if (material is null) return int.MaxValue;
        if (material.MaxBannerWidthCm > 0) return material.MaxBannerWidthCm;
        if (material.WidthCm > 0) return material.WidthCm;
        return int.MaxValue;
    }

    // ── Eyelet (malje) addon ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<decimal> GetEyeletPriceNokAsync()
    {
        var p = await _db.PricingParameters
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Key, x => x.Value);
        return p.GetValueOrDefault("eyelet_price_nok", 0m);
    }

    /// <inheritdoc/>
    public async Task<(decimal FeeNok, int Count)> CalculateEyeletCostAsync(
        int widthCm, int heightCm, EyeletOption option)
    {
        if (option == EyeletOption.None) return (0m, 0);

        var count = EyeletCalculator.CountEyelets(widthCm, heightCm, option);
        if (count == 0) return (0m, 0);

        var pricePerEyelet = await GetEyeletPriceNokAsync();
        return (decimal.Round(pricePerEyelet * count, 2), count);
    }

    // ── Panel helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Compute the number of panels needed to cover <paramref name="bannerWidthCm"/> given
    /// each panel can print up to <paramref name="maxWidthPerPanel"/> cm and adjacent
    /// panels share an <paramref name="overlapCm"/> cm seam. Always returns at least 1.
    /// </summary>
    public static int PanelsNeeded(int bannerWidthCm, int maxWidthPerPanel, int overlapCm)
    {
        if (bannerWidthCm <= 0 || maxWidthPerPanel <= 0) return 1;
        if (bannerWidthCm <= maxWidthPerPanel) return 1;
        // overlapCm must be strictly less than maxWidthPerPanel for the formula to make
        // sense — otherwise additional panels add no coverage. Clamp defensively.
        var safeOverlap = Math.Max(0, Math.Min(overlapCm, maxWidthPerPanel - 1));
        // panels = ⌈(bannerWidth − overlap) / (maxWidth − overlap)⌉
        var numerator = bannerWidthCm - safeOverlap;
        var denominator = maxWidthPerPanel - safeOverlap;
        var panels = (numerator + denominator - 1) / denominator; // integer ceil
        return Math.Max(1, panels);
    }
}
