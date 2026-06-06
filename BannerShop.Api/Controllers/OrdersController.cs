using System.Security.Claims;
using BannerShop.Api.Models.Orders;
using BannerShop.Api.Services.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BannerShop.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orders;

    public OrdersController(IOrderService orders) => _orders = orders;

    // ── POST /api/orders/draft ───────────────────────────────────────────────
    [HttpPost("draft")]
    public async Task<IActionResult> CreateDraft([FromBody] CreateOrderDraftRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var result = await _orders.CreateDraftAsync(userId, req, ct);
        if (!result.Success)
            return BadRequest(new { error = result.Error });

        return Ok(new CreateOrderDraftResponseDto
        {
            OrderId = result.OrderId,
            ClientSecret = result.ClientSecret,
            TotalNok = result.TotalNok,
            Breakdown = result.Breakdown
        });
    }

    // ── GET /api/orders?page=1&pageSize=20 ───────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> ListMine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        var paged = await _orders.ListMineAsync(userId, page, pageSize, ct);
        return Ok(paged);
    }

    // ── GET /api/orders/{id} ─────────────────────────────────────────────────
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetMine(int id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        var dto = await _orders.GetMineAsync(userId, id, ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    // ── POST /api/orders/{id}/cancel ─────────────────────────────────────────
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        var result = await _orders.CancelMineAsync(userId, id, ct);
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(result.Order);
    }

    private int GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : 0;
    }
}
