using BannerShop.Api.Services.Orders;
using BannerShop.Api.Services.SystemSettings;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Controllers.Admin;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/orders/{orderId:int}/items/{itemId:int}/upscale")]
public sealed class AdminOrderUpscalesController(
    BannerShopDbContext db, FalOrderUpscaleService upscales, ISystemSettingsService settings,
    ILogger<AdminOrderUpscalesController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Prepare(int orderId, int itemId, [FromQuery] int scale, CancellationToken ct)
    {
        var source = await SourceAsync(orderId, itemId, ct);
        if (source is null) return NotFound(new { error = "Fant ikke bannerets originalfil." });
        try
        {
            var ready = await upscales.PrepareAsync(source, scale, await settings.GetValueAsync("fal_api_key", ct), ct);
            return Ok(new { ready });
        }
        catch (UpscaleException ex) { return StatusCode(ex.StatusCode, new { error = ex.Message }); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Order upscale failed for order {OrderId}, item {ItemId}, scale {Scale}", orderId, itemId, scale);
            return StatusCode(502, new { error = "Kunne ikke oppskalere bildet. Prøv igjen for å fortsette samme jobb." });
        }
    }

    [HttpGet("download")]
    public async Task<IActionResult> Download(int orderId, int itemId, [FromQuery] int scale, CancellationToken ct)
    {
        var source = await SourceAsync(orderId, itemId, ct);
        if (source is null) return NotFound();
        try
        {
            var path = await upscales.GetCachedPathAsync(source, scale, ct);
            if (path is null) return NotFound(new { error = "Oppskaleringen er ikke klar ennå." });
            return PhysicalFile(path, "image/png", $"ordre-{orderId}-banner-{itemId}-{scale}x.png", enableRangeProcessing: true);
        }
        catch (UpscaleException ex) { return StatusCode(ex.StatusCode, new { error = ex.Message }); }
    }

    private async Task<string?> SourceAsync(int orderId, int itemId, CancellationToken ct)
    {
        // Resolve only server-owned paths, and scope the item to its non-deleted order.
        var item = await db.OrderItems.AsNoTracking()
            .Include(i => i.BannerDesign).Include(i => i.DesignRequest)
            .FirstOrDefaultAsync(i => i.OrderId == orderId && i.Id == itemId && !i.Order.Deleted, ct);
        return item?.BannerDesign?.StoragePath
            ?? item?.DesignRequest?.FinalCroppedStoragePath
            ?? item?.DesignRequest?.AiResultStoragePath;
    }
}
