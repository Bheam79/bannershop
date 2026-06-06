using BannerShop.Api.Models.Catalog;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/pricing-parameters")]
[Authorize(Roles = "Admin")]
public class AdminPricingController : ControllerBase
{
    private readonly BannerShopDbContext _db;

    public AdminPricingController(BannerShopDbContext db) => _db = db;

    // ── GET /api/admin/pricing-parameters ────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var params_ = await _db.PricingParameters.OrderBy(p => p.Id).ToListAsync();
        return Ok(params_.Select(p => new PricingParameterDto
        {
            Id = p.Id,
            Name = p.Name,
            Key = p.Key,
            Value = p.Value,
            Description = p.Description
        }));
    }

    // ── PUT /api/admin/pricing-parameters/{id} ────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePricingParameterRequest req)
    {
        var param = await _db.PricingParameters.FindAsync(id);
        if (param == null) return NotFound();

        param.Value = req.Value;
        await _db.SaveChangesAsync();

        return Ok(new PricingParameterDto
        {
            Id = param.Id,
            Name = param.Name,
            Key = param.Key,
            Value = param.Value,
            Description = param.Description
        });
    }
}
