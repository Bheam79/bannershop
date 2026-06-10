using System.Security.Claims;
using BannerShop.Api.Models.Orders;
using BannerShop.Api.Services.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static BannerShop.Api.Services.Orders.OrderActionErrorType;

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

    // ── DELETE /api/orders/{id} ──────────────────────────────────────────────
    /// <summary>
    /// BANNERSH-185: customer-initiated soft-delete for unpaid orders (Draft /
    /// PendingPayment / Cancelled). Hides the order from the customer's listing
    /// and cancels any open Stripe PaymentIntent. Paid orders are not deletable.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        var result = await _orders.DeleteMineAsync(userId, id, ct);
        if (!result.Success)
            return result.Error == "Order not found."
                ? NotFound(new { error = result.Error })
                : BadRequest(new { error = result.Error });
        return NoContent();
    }

    // ── POST /api/orders/{id}/retry-payment ──────────────────────────────────
    /// <summary>
    /// BANNERSH-185: customer "Betal nå" action — re-opens an existing
    /// PendingPayment order for payment, returning a Stripe client secret usable
    /// with <c>confirmCardPayment</c> on the frontend. When the order is already
    /// paid, the response carries <c>alreadyPaid=true</c> and no client secret
    /// so the frontend can route straight to the confirmation page.
    /// </summary>
    [HttpPost("{id:int}/retry-payment")]
    public async Task<IActionResult> RetryPayment(int id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        var result = await _orders.RetryPaymentAsync(userId, id, ct);
        if (!result.Success)
            return result.Error == "Order not found."
                ? NotFound(new { error = result.Error })
                : BadRequest(new { error = result.Error });
        return Ok(new
        {
            orderId = result.OrderId,
            clientSecret = result.ClientSecret,
            totalNok = result.TotalNok,
            alreadyPaid = result.AlreadyPaid,
        });
    }

    // ── POST /api/orders/{id}/mock-pay ───────────────────────────────────────
    /// <summary>
    /// BANNERSH-182: testing-only override that flips a Draft/PendingPayment
    /// order to Paid without going through Stripe. Used by the checkout's
    /// "Marker som betalt (testmodus)" modal so the operator can exercise the
    /// post-payment flow end-to-end (production rows, confirmation email,
    /// redirect to the confirmation page) when Stripe is not configured on
    /// the frontend. Gated by <c>Testing:EnableMockPayment</c> + a
    /// configurable password (<c>Testing:MockPaymentPassword</c>).
    /// </summary>
    [HttpPost("{id:int}/mock-pay")]
    public async Task<IActionResult> MockPay(int id, [FromBody] MockPayRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var result = await _orders.MockMarkPaidAsync(userId, id, req.Password, ct);
        if (!result.Success)
            return result.Error == "Invalid mock-payment password."
                ? Unauthorized(new { error = result.Error })
                : NotFound(new { error = result.Error });
        return Ok(result.Order);
    }

    // ── POST /api/orders/{id}/approve ────────────────────────────────────────
    /// <summary>
    /// Customer approves the AI or manual design preview for this order.
    /// Advances <c>OrderState</c> from <c>CustomerApproval</c> to <c>InProduction</c>
    /// and mirrors the approval on the linked DesignRequest (if any).
    /// Returns 422 when the current state is not <c>CustomerApproval</c>.
    /// </summary>
    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> ApproveDesign(int id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        var result = await _orders.ApproveDesignAsync(id, userId, ct);
        if (!result.Success)
            return result.ErrorType == InvalidTransition
                ? UnprocessableEntity(new { error = result.Error })
                : NotFound(new { error = result.Error });
        return Ok(result.Order);
    }

    private int GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : 0;
    }
}
