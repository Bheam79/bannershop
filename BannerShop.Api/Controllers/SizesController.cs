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

    // ── GET /api/sizes?customWidthCm=X&customHeightCm=Y ──────────────────────
    [HttpGet]
    public async Task<IActionResult> GetSizes(
        [FromQuery] int? customWidthCm = null,
        [FromQuery] int? customHeightCm = null)
    {
        var sizes = await _db.BannerSizes
            .Include(s => s.Material)
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        var result = new List<BannerSizeDto>(sizes.Count);
        foreach (var s in sizes)
        {
            var price = await _pricing.CalculatePriceAsync(s, customWidthCm, customHeightCm);
            result.Add(ToDto(s, price));
        }

        return Ok(result);
    }

    // ── GET /api/sizes/eyelet-price ───────────────────────────────────────────
    /// <summary>
    /// Returns the current price per eyelet (malje) in NOK.
    /// Used by the frontend to compute eyelet addon costs before order submission.
    /// </summary>
    [HttpGet("eyelet-price")]
    public async Task<IActionResult> GetEyeletPrice()
    {
        var pricePerEyelet = await _pricing.GetEyeletPriceNokAsync();
        return Ok(new { pricePerEyeletNok = pricePerEyelet });
    }

    // ── GET /api/sizes/{id}/price?customWidthCm=X&customHeightCm=Y ───────────
    [HttpGet("{id:int}/price")]
    public async Task<IActionResult> GetPrice(
        int id,
        [FromQuery] int? customWidthCm = null,
        [FromQuery] int? customHeightCm = null)
    {
        // Include Material so PricingService can apply the multi-panel multiplier
        // (BANNERSH-88) when the requested width exceeds Material.MaxBannerWidthCm.
        var size = await _db.BannerSizes
            .Include(s => s.Material)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (size == null) return NotFound();

        if (size.IsCustomWidth && customWidthCm is null)
            return BadRequest(new { error = "customWidthCm is required for custom-width banner sizes." });
        if (size.IsCustomHeight && customHeightCm is null)
            return BadRequest(new { error = "customHeightCm is required for custom-height banner sizes." });

        var price = await _pricing.CalculatePriceAsync(size, customWidthCm, customHeightCm);
        return Ok(new PriceResponseDto
        {
            SizeId = size.Id,
            CustomWidthCm = customWidthCm,
            CustomHeightCm = customHeightCm,
            PriceNok = price
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static BannerSizeDto ToDto(BannerShop.Core.Entities.BannerSize s, decimal price) => new()
    {
        Id = s.Id,
        WidthCm = s.WidthCm,
        HeightCm = s.HeightCm,
        IsCustomWidth = s.IsCustomWidth,
        IsCustomHeight = s.IsCustomHeight,
        Name = s.Name,
        IsActive = s.IsActive,
        MaterialId = s.MaterialId,
        Material = new MaterialDto
        {
            Id = s.Material.Id,
            Name = s.Material.Name,
            WidthCm = s.Material.WidthCm,
            MaxBannerWidthCm = s.Material.MaxBannerWidthCm > 0 ? s.Material.MaxBannerWidthCm : s.Material.WidthCm,
            WeightGsm = s.Material.WeightGsm,
            PricePerSqm = s.Material.PricePerSqm,
            AvailableFrom = s.Material.AvailableFrom
        },
        FixedPrice = s.FixedPrice,
        SortOrder = s.SortOrder,
        CalculatedPrice = price,
        AvailableFrom = s.Material.AvailableFrom
    };
}
