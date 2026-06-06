using BannerShop.Api.Models.Orders;
using BannerShop.Api.Services.Orders;
using BannerShop.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BannerShop.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/orders")]
[Authorize(Roles = "Admin")]
public class AdminOrdersController : ControllerBase
{
    private readonly IOrderService _orders;

    public AdminOrdersController(IOrderService orders) => _orders = orders;

    // ── GET /api/admin/orders ────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] OrderStatus? status = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var paged = await _orders.ListAllAsync(new AdminOrderFilter
        {
            Status = status,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Search = search,
            Page = page,
            PageSize = pageSize
        }, ct);
        return Ok(paged);
    }

    // ── GET /api/admin/orders/{id} ───────────────────────────────────────────
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var dto = await _orders.GetAnyAsync(id, ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    // ── PUT /api/admin/orders/{id}/status ────────────────────────────────────
    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateOrderStatusRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var result = await _orders.UpdateStatusAsync(id, req.Status, ct);
        if (!result.Success) return NotFound(new { error = result.Error });
        return Ok(result.Order);
    }

    // ── PUT /api/admin/orders/{id}/items/{itemId}/production ─────────────────
    [HttpPut("{id:int}/items/{itemId:int}/production")]
    public async Task<IActionResult> UpdateProduction(
        int id, int itemId,
        [FromBody] UpdateProductionRequest req,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var result = await _orders.UpdateProductionAsync(id, itemId, req.Stage, req.Notes, ct);
        if (!result.Success) return NotFound(new { error = result.Error });
        return Ok(result.Order);
    }

    // ── POST /api/admin/orders/{id}/shipping ─────────────────────────────────
    [HttpPost("{id:int}/shipping")]
    public async Task<IActionResult> SetShipping(int id, [FromBody] SetShippingRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var result = await _orders.SetShippingAsync(id, req, ct);
        if (!result.Success) return NotFound(new { error = result.Error });
        return Ok(result.Order);
    }
}
