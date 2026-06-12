using BannerShop.Api.Models.Orders;
using BannerShop.Core.Enums;

namespace BannerShop.Api.Services.Orders;

/// <summary>
/// Admin-only order operations (BANNERSH-199 split). Separated from
/// <see cref="IOrderService"/> so the controller surface and back-office wiring stays
/// distinct from customer-facing ordering/checkout/webhook logic.
/// </summary>
public interface IAdminOrderService
{
    /// <summary>Paged admin listing across all customers.</summary>
    Task<PagedResult<OrderListItemDto>> ListAllAsync(AdminOrderFilter filter, CancellationToken ct = default);

    /// <summary>Fetch any order detail by id (no customer ownership check).</summary>
    Task<OrderDetailDto?> GetAnyAsync(int orderId, CancellationToken ct = default);

    /// <summary>Validates the requested transition against <c>AllowedTransitions</c> and applies it.</summary>
    Task<OrderActionResult> UpdateStatusAsync(int orderId, OrderStatus status, CancellationToken ct = default);

    /// <summary>Records a production stage event for a single item and may promote the order's overall status.</summary>
    Task<OrderActionResult> UpdateProductionAsync(int orderId, int itemId, ProductionStage stage, string? notes, CancellationToken ct = default);

    /// <summary>Creates or updates the order's <c>ShipmentTracking</c> row and transitions the order to <c>Shipped</c>.</summary>
    Task<OrderActionResult> SetShippingAsync(int orderId, SetShippingRequest req, CancellationToken ct = default);

    /// <summary>
    /// Advances <see cref="Core.Entities.Order.OrderState"/> from its current value to
    /// <paramref name="next"/>, validating the transition against the order's
    /// <see cref="Core.Enums.OrderType"/> using <see cref="Core.Helpers.OrderStateHelper"/>.
    /// Returns a transition-error result when the move is not permitted.
    /// </summary>
    Task<OrderActionResult> AdvanceStateAsync(int orderId, OrderState next, CancellationToken ct = default);

    /// <summary>
    /// Captures a previously authorized Stripe PaymentIntent for the order.
    /// Call this before starting production to confirm the payment still goes through.
    /// Only valid when the order has a StripePaymentIntentId and is in Paid /
    /// InProduction / ReadyToShip status. Returns a failure result if Stripe rejects
    /// the capture (e.g. auth expired — admin must contact the customer).
    /// </summary>
    Task<OrderActionResult> CapturePaymentAsync(int orderId, CancellationToken ct = default);
}
