using BannerShop.Api.Models.Catalog;
using BannerShop.Core.Entities;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/materials")]
[Authorize(Roles = "Admin")]
public class AdminMaterialsController : ControllerBase
{
    private readonly BannerShopDbContext _db;

    public AdminMaterialsController(BannerShopDbContext db) => _db = db;

    // ── GET /api/admin/materials ──────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var materials = await _db.Materials.OrderBy(m => m.Id).ToListAsync();
        return Ok(materials.Select(ToDto));
    }

    // ── POST /api/admin/materials ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveMaterialRequest req)
    {
        var m = new Material
        {
            Name = req.Name,
            WidthCm = req.WidthCm,
            WeightGsm = req.WeightGsm,
            PricePerSqm = req.PricePerSqm,
            AvailableFrom = req.AvailableFrom
        };
        _db.Materials.Add(m);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new { }, ToDto(m));
    }

    // ── PUT /api/admin/materials/{id} ─────────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveMaterialRequest req)
    {
        var m = await _db.Materials.FindAsync(id);
        if (m == null) return NotFound();

        m.Name = req.Name;
        m.WidthCm = req.WidthCm;
        m.WeightGsm = req.WeightGsm;
        m.PricePerSqm = req.PricePerSqm;
        m.AvailableFrom = req.AvailableFrom;
        await _db.SaveChangesAsync();
        return Ok(ToDto(m));
    }

    // ── DELETE /api/admin/materials/{id} ──────────────────────────────────────
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var m = await _db.Materials.FindAsync(id);
        if (m == null) return NotFound();

        // Check if material is in use
        var inUse = await _db.BannerSizes.AnyAsync(s => s.MaterialId == id);
        if (inUse)
            return Conflict(new { error = "Cannot delete material that is referenced by banner sizes." });

        _db.Materials.Remove(m);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static MaterialDto ToDto(Material m) => new()
    {
        Id = m.Id,
        Name = m.Name,
        WidthCm = m.WidthCm,
        WeightGsm = m.WeightGsm,
        PricePerSqm = m.PricePerSqm,
        AvailableFrom = m.AvailableFrom
    };
}
