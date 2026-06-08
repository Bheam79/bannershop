using BannerShop.Api.Models.Catalog;
using BannerShop.Api.Services;
using BannerShop.Core.Entities;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/sizes")]
[Authorize(Roles = "Admin")]
public class AdminSizesController : ControllerBase
{
    private readonly BannerShopDbContext _db;
    private readonly IPricingService _pricing;

    public AdminSizesController(BannerShopDbContext db, IPricingService pricing)
    {
        _db = db;
        _pricing = pricing;
    }

    // ── GET /api/admin/sizes ──────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var sizes = await _db.BannerSizes
            .Include(s => s.Material)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        var result = new List<BannerSizeDto>(sizes.Count);
        foreach (var s in sizes)
        {
            var price = await _pricing.CalculatePriceAsync(s);
            result.Add(ToDto(s, price));
        }
        return Ok(result);
    }

    // ── POST /api/admin/sizes ─────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveBannerSizeRequest req)
    {
        if (!await _db.Materials.AnyAsync(m => m.Id == req.MaterialId))
            return BadRequest(new { error = "Material not found." });

        var s = new BannerSize
        {
            WidthCm = req.WidthCm,
            HeightCm = req.HeightCm,
            IsCustomWidth = req.IsCustomWidth,
            Name = req.Name,
            IsActive = req.IsActive,
            MaterialId = req.MaterialId,
            FixedPrice = req.FixedPrice,
            SortOrder = req.SortOrder
        };
        _db.BannerSizes.Add(s);
        await _db.SaveChangesAsync();

        await _db.Entry(s).Reference(x => x.Material).LoadAsync();
        var price = await _pricing.CalculatePriceAsync(s);
        return CreatedAtAction(nameof(GetAll), new { }, ToDto(s, price));
    }

    // ── PUT /api/admin/sizes/{id} ─────────────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveBannerSizeRequest req)
    {
        var s = await _db.BannerSizes.Include(x => x.Material).FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound();

        if (req.MaterialId != s.MaterialId && !await _db.Materials.AnyAsync(m => m.Id == req.MaterialId))
            return BadRequest(new { error = "Material not found." });

        s.WidthCm = req.WidthCm;
        s.HeightCm = req.HeightCm;
        s.IsCustomWidth = req.IsCustomWidth;
        s.Name = req.Name;
        s.IsActive = req.IsActive;
        s.MaterialId = req.MaterialId;
        s.FixedPrice = req.FixedPrice;
        s.SortOrder = req.SortOrder;
        await _db.SaveChangesAsync();

        // Reload material if it changed
        if (s.Material.Id != req.MaterialId)
            await _db.Entry(s).Reference(x => x.Material).LoadAsync();

        var price = await _pricing.CalculatePriceAsync(s);
        return Ok(ToDto(s, price));
    }

    // ── DELETE /api/admin/sizes/{id} ──────────────────────────────────────────
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var s = await _db.BannerSizes.FindAsync(id);
        if (s == null) return NotFound();

        var inUse = await _db.OrderItems.AnyAsync(i => i.BannerSizeId == id);
        if (inUse)
            return Conflict(new { error = "Cannot delete a size that is referenced by existing orders." });

        _db.BannerSizes.Remove(s);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static BannerSizeDto ToDto(BannerSize s, decimal price) => new()
    {
        Id = s.Id,
        WidthCm = s.WidthCm,
        HeightCm = s.HeightCm,
        IsCustomWidth = s.IsCustomWidth,
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
