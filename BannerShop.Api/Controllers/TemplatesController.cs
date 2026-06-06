using BannerShop.Api.Models.Templates;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Controllers;

/// <summary>
/// Public list of pre-defined banner templates (celebration categories) used as a
/// starting point in the banner builder. Templates are seeded — see BANNERSH-17.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class TemplatesController : ControllerBase
{
    private readonly BannerShopDbContext _db;

    public TemplatesController(BannerShopDbContext db)
    {
        _db = db;
    }

    // ── GET /api/templates ────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetTemplates(CancellationToken ct)
    {
        var rows = await _db.BannerTemplates
            .AsNoTracking()
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .Select(t => new BannerTemplateDto
            {
                Id = t.Id,
                Category = t.Category.ToString(),
                NameNb = t.NameNb,
                NameEn = t.NameEn,
                SortOrder = t.SortOrder
            })
            .ToListAsync(ct);

        return Ok(rows);
    }
}
