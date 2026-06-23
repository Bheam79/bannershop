using BannerShop.Api.Models.Catalog;
using BannerShop.Api.Services;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SizesController : ControllerBase
{
    private readonly BannerShopDbContext _db;
    private readonly IPricingService _pricing;

    public SizesController(BannerShopDbContext db, IPricingService pricing)
    {
        _db = db;
        _pricing = pricing;
    }

    // ── GET /api/sizes ────────────────────────────────────────────────────────
    /// <summary>
    /// Returns all active banner pricing rules. With BANNERSH-255 each row describes
    /// a (material × width-range × height-range) combo plus a pricing formula —
    /// callers that need a price for specific dimensions should use the
    /// <c>/api/sizes/price</c> endpoint instead, which finds the cheapest matching
    /// rule across all materials.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSizes()
    {
        var sizes = await _db.BannerSizes
            .Include(s => s.Material)
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        var result = new List<BannerSizeDto>(sizes.Count);
        foreach (var s in sizes)
        {
            // Sample at the lower-left of the range for the list payload.
            var sampleW = s.MinWidthCm  > 0 ? s.MinWidthCm  : 1;
            var sampleH = s.MinHeightCm > 0 ? s.MinHeightCm : 1;
            var price = await _pricing.CalculatePriceAsync(s, sampleW, sampleH);
            result.Add(ToDto(s, price));
        }

        return Ok(result);
    }

    // ── GET /api/sizes/eyelet-price ───────────────────────────────────────────
    [HttpGet("eyelet-price")]
    public async Task<IActionResult> GetEyeletPrice()
    {
        var pricePerEyelet = await _pricing.GetEyeletPriceNokAsync();
        return Ok(new { pricePerEyeletNok = pricePerEyelet });
    }

    // ── GET /api/sizes/price?widthCm=X&heightCm=Y[&materialId=Z] ──────────────
    /// <summary>
    /// Finds the cheapest matching pricing rule for the requested banner dimensions.
    /// Returns 404 when no rule covers the requested (width × height [× material]).
    /// </summary>
    [HttpGet("price")]
    public async Task<IActionResult> GetPrice(
        [FromQuery] int widthCm,
        [FromQuery] int heightCm,
        [FromQuery] int? materialId = null,
        CancellationToken ct = default)
    {
        if (widthCm <= 0 || heightCm <= 0)
            return BadRequest(new { error = "widthCm and heightCm must be positive." });

        var match = await _pricing.FindCheapestAsync(widthCm, heightCm, materialId, ct);
        if (match is null)
            return NotFound(new { error = "No matching banner size rule for the requested dimensions." });

        return Ok(new PriceResponseDto
        {
            SizeId = match.Rule.Id,
            WidthCm = widthCm,
            HeightCm = heightCm,
            MaterialId = match.Rule.MaterialId,
            // Round to 2 decimal places — consistent with ResolveBannerProductionCostAsync.
            PriceNok = decimal.Round(match.PriceNok, 2)
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static BannerSizeDto ToDto(BannerShop.Core.Entities.BannerSize s, decimal price) => new()
    {
        Id = s.Id,
        Name = s.Name,
        IsActive = s.IsActive,
        MaterialId = s.MaterialId,
        SortOrder = s.SortOrder,
        MinWidthCm = s.MinWidthCm,
        MaxWidthCm = s.MaxWidthCm,
        MinHeightCm = s.MinHeightCm,
        MaxHeightCm = s.MaxHeightCm,
        PricingHeightCm = s.PricingHeightCm,
        PricingMultiplier = s.PricingMultiplier,
        FixedPrice = s.FixedPrice,
        Material = new MaterialDto
        {
            Id = s.Material.Id,
            Name = s.Material.Name,
            WidthCm = s.Material.WidthCm,
            WeightGsm = s.Material.WeightGsm,
            PricePerSqm = s.Material.PricePerSqm,
            AvailableFrom = s.Material.AvailableFrom
        },
        CalculatedPrice = price,
        AvailableFrom = s.Material.AvailableFrom
    };
}
