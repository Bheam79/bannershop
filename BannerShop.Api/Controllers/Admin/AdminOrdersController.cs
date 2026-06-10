using BannerShop.Api.Models.Orders;
using BannerShop.Api.Services.Orders;
using BannerShop.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static BannerShop.Api.Services.Orders.OrderActionErrorType;

namespace BannerShop.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/orders")]
[Authorize(Roles = "Admin")]
public class AdminOrdersController : ControllerBase
{
    private readonly IAdminOrderService _orders;

    public AdminOrdersController(IAdminOrderService orders) => _orders = orders;

    // ── GET /api/admin/orders ────────────────────────────────────────────────
    /// <summary>
    /// Admin orders list. By default (BANNERSH-139), credit-pack purchases are hidden
    /// so the production team only sees orders they need to print. Pass
    /// <paramref name="includeCreditPacks"/>=true to see them too (used by the
    /// transaction-reports view), or filter by <c>orderType=CreditPack</c> directly.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] OrderStatus? status = null,
        [FromQuery] OrderType? orderType = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool includeCreditPacks = false,
        [FromQuery] bool excludeZeroValueAiOrders = true,
        CancellationToken ct = default)
    {
        var paged = await _orders.ListAllAsync(new AdminOrderFilter
        {
            Status = status,
            OrderType = orderType,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Search = search,
            Page = page,
            PageSize = pageSize,
            IncludeCreditPacks = includeCreditPacks,
            ExcludeZeroValueAiOrders = excludeZeroValueAiOrders
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
        if (!result.Success)
            return result.ErrorType == InvalidTransition
                ? UnprocessableEntity(new { error = result.Error })
                : NotFound(new { error = result.Error });
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
        if (!result.Success)
            return result.ErrorType == InvalidTransition
                ? UnprocessableEntity(new { error = result.Error })
                : NotFound(new { error = result.Error });
        return Ok(result.Order);
    }

    // ── POST /api/admin/orders/{id}/advance ──────────────────────────────────
    /// <summary>
    /// Advances the order's <c>OrderState</c> to the requested next state, validating
    /// the transition against the order's type via <c>OrderStateHelper</c>.
    /// Returns 422 when the transition is not permitted.
    /// </summary>
    [HttpPost("{id:int}/advance")]
    public async Task<IActionResult> Advance(int id, [FromBody] AdvanceOrderStateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var result = await _orders.AdvanceStateAsync(id, req.Next, ct);
        if (!result.Success)
            return result.ErrorType == InvalidTransition
                ? UnprocessableEntity(new { error = result.Error })
                : NotFound(new { error = result.Error });
        return Ok(result.Order);
    }

    // ── POST /api/admin/orders/{id}/advance-state ────────────────────────────
    /// <summary>
    /// Unified orders API variant of the advance endpoint. Accepts <c>{ toState }</c>
    /// in the request body (matching the BANNERSH-110 API surface) and delegates to
    /// the same <see cref="IAdminOrderService.AdvanceStateAsync"/> implementation.
    /// Returns 422 when the transition is not permitted.
    /// </summary>
    [HttpPost("{id:int}/advance-state")]
    public async Task<IActionResult> AdvanceState(int id, [FromBody] AdvanceOrderStateByToStateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var result = await _orders.AdvanceStateAsync(id, req.ToState, ct);
        if (!result.Success)
            return result.ErrorType == InvalidTransition
                ? UnprocessableEntity(new { error = result.Error })
                : NotFound(new { error = result.Error });
        return Ok(result.Order);
    }
}
