using BannerShop.Core.Entities;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Services;

public class PricingService : IPricingService
{
    private readonly BannerShopDbContext _db;

    public PricingService(BannerShopDbContext db) => _db = db;

    public async Task<decimal> CalculatePriceAsync(BannerSize size, int? customWidthCm = null)
    {
        // Fixed-price sizes (e.g. 300×180 cm at 699 NOK)
        if (size.FixedPrice.HasValue)
            return size.FixedPrice.Value;

        // Load pricing parameters as a dictionary
        var p = await _db.PricingParameters
            .ToDictionaryAsync(x => x.Key, x => x.Value);

        var basePricePerSqm = p.GetValueOrDefault("base_price_per_sqm", 180m);
        var minimumPrice    = p.GetValueOrDefault("minimum_price",        399m);
        var customSurcharge = p.GetValueOrDefault("custom_width_surcharge", 150m);
        var hemFlatFee      = p.GetValueOrDefault("hem_and_eyelets_flat_fee", 0m);

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
        return basePrice + hemFlatFee + surcharge;
    }
}
