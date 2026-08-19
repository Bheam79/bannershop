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

    /// <inheritdoc />
    /// <remarks>
    /// BANNERSH-255: the formula is now
    ///   <c>price = max(minimum_price, widthCm × pricingHeight × pricePerSqm) × pricingMultiplier</c>
    /// where <c>pricingHeight</c> and <c>pricingMultiplier</c> come from the matching
    /// pricing rule.  A non-null <see cref="BannerSize.FixedPrice"/> short-circuits the
    /// formula entirely (used for printed standard sizes).
    /// </remarks>
    public async Task<decimal> CalculatePriceAsync(BannerSize rule, int widthCm, int heightCm)
    {
        if (rule.FixedPrice.HasValue)
            return rule.FixedPrice.Value;

        var p = await _db.PricingParameters
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Key, x => x.Value);

        return CalculatePrice(rule, widthCm, heightCm, p);
    }

    private static decimal CalculatePrice(BannerSize rule, int widthCm, int heightCm, IReadOnlyDictionary<string, decimal> pricingParams)
    {
        if (rule.FixedPrice.HasValue)
            return rule.FixedPrice.Value;

        var basePricePerSqm = rule.Material?.PricePerSqm
            ?? pricingParams.GetValueOrDefault("base_price_per_sqm", 180m);
        var minimumPrice = pricingParams.GetValueOrDefault("minimum_price", 399m);

        // Defensive clamps — bad data shouldn't crash the price calc.
        if (widthCm <= 0 || heightCm <= 0)
            return minimumPrice;

        var pricingHeight = rule.PricingHeightCm > 0 ? rule.PricingHeightCm : heightCm;
        var multiplier = rule.PricingMultiplier > 0 ? rule.PricingMultiplier : 1;

        var areaSqm = (widthCm / 100m) * (pricingHeight / 100m);
        var basePrice = Math.Max(minimumPrice, areaSqm * basePricePerSqm);
        return basePrice * multiplier;
    }

    /// <inheritdoc />
    public async Task<PriceMatch?> FindCheapestAsync(int widthCm, int heightCm, int? materialId = null, CancellationToken ct = default)
    {
        if (widthCm <= 0 || heightCm <= 0) return null;

        var query = _db.BannerSizes
            .AsNoTracking()
            .Include(s => s.Material)
            .Where(s => s.IsActive
                     && s.MinWidthCm <= widthCm && widthCm <= s.MaxWidthCm
                     && s.MinHeightCm <= heightCm && heightCm <= s.MaxHeightCm);

        if (materialId.HasValue)
            query = query.Where(s => s.MaterialId == materialId.Value);

        var rules = await query.ToListAsync(ct);
        if (rules.Count == 0) return null;

        // Pricing parameters are the same for every candidate rule — fetch once
        // instead of re-querying inside the loop (was N+1 for N matching rules).
        var pricingParams = await _db.PricingParameters
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Key, x => x.Value, ct);

        PriceMatch? best = null;
        foreach (var r in rules)
        {
            var price = CalculatePrice(r, widthCm, heightCm, pricingParams);
            if (best is null || price < best.PriceNok)
                best = new PriceMatch(r, price);
        }
        return best;
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

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ItemPriceResult>> CalculateItemPricingAsync(
        IReadOnlyList<ItemPricingInput> items, CancellationToken ct = default)
    {
        if (items.Count == 0) return Array.Empty<ItemPriceResult>();

        // Pricing parameters are the same for every line — fetch once instead of
        // re-querying per item (was up to 2×N queries for an N-item order/cart).
        var pricingParams = await _db.PricingParameters
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        var pricePerEyelet = pricingParams.GetValueOrDefault("eyelet_price_nok", 0m);

        var results = new List<ItemPriceResult>(items.Count);
        foreach (var item in items)
        {
            var unitPrice = CalculatePrice(item.Rule, item.WidthCm, item.HeightCm, pricingParams);

            var eyeletFee = 0m;
            var eyeletCount = 0;
            if (item.EyeletOption != EyeletOption.None)
            {
                eyeletCount = EyeletCalculator.CountEyelets(item.WidthCm, item.HeightCm, item.EyeletOption);
                if (eyeletCount > 0)
                    eyeletFee = decimal.Round(pricePerEyelet * eyeletCount, 2);
            }

            results.Add(new ItemPriceResult(unitPrice, eyeletFee, eyeletCount));
        }
        return results;
    }
}
