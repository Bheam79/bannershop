using BannerShop.Api.Models.Catalog;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MaterialsController : ControllerBase
{
    private readonly BannerShopDbContext _db;

    public MaterialsController(BannerShopDbContext db) => _db = db;

    // ── GET /api/materials ────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetMaterials()
    {
        var materials = await _db.Materials
            .OrderBy(m => m.Id)
            .ToListAsync();

        return Ok(materials.Select(m => new MaterialDto
        {
            Id = m.Id,
            Name = m.Name,
            WidthCm = m.WidthCm,
            MaxBannerWidthCm = m.MaxBannerWidthCm > 0 ? m.MaxBannerWidthCm : m.WidthCm,
            WeightGsm = m.WeightGsm,
            PricePerSqm = m.PricePerSqm,
            AvailableFrom = m.AvailableFrom
        }));
    }
}
