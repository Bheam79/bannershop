using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Services.Shipping;

/// <summary>
/// Fallback shipping calculator used when Bring credentials are not configured.
/// Returns a deterministic, weight-based estimate so the rest of the checkout flow remains usable in dev.
/// </summary>
public class MockShippingService : IShippingService
{
    private readonly BannerShopDbContext _db;
    private readonly ILogger<MockShippingService> _logger;

    public MockShippingService(BannerShopDbContext db, ILogger<MockShippingService> logger)
    {
        _db = db;
        _logger = logger;
        _logger.LogWarning(
            "MockShippingService is in use — Bring API credentials are not configured. Returned shipping prices are estimates.");
    }

    public async Task<ShippingQuote> CalculateAsync(
        string toPostalCode,
        string? toCity,
        ParcelDimensions parcel,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toPostalCode))
            throw new ArgumentException("Destination postal code is required.", nameof(toPostalCode));

        // Simple model: base 149 NOK + 35 NOK/kg + 0.4 NOK per cm of length over 100 cm.
        var weight = Math.Max(0.5m, parcel.WeightKg);
        var lengthSurcharge = Math.Max(0m, parcel.LengthCm - 100m) * 0.4m;
        var standardCost = decimal.Round(149m + (weight * 35m) + lengthSurcharge, 2);

        var expressFee = await _db.PricingParameters
            .AsNoTracking()
            .Where(x => x.Key == "express_fee")
            .Select(x => (decimal?)x.Value)
            .FirstOrDefaultAsync(ct) ?? 500m;

        var standard = new ShippingOption(standardCost, 3, "MOCK_STANDARD", "Mock standard parcel");
        var express  = new ShippingOption(standardCost + expressFee, 1, "MOCK_STANDARD", "Mock standard parcel");
        return new ShippingQuote(standard, express);
    }
}
