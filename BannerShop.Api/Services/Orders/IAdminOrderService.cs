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

    /// <summary>
    /// Manually marks a Draft or PendingPayment order as Paid — used by admins when
    /// the Stripe <c>payment_intent.amount_capturable_updated</c> webhook was not
    /// delivered (e.g. webhook secret not yet configured, or Stripe dashboard event
    /// subscription missing). Applies all the same side-effects as the webhook path:
    /// production rows seeded, confirmation email sent, AI credits granted (when
    /// applicable). The Stripe authorization hold is NOT captured here — use
    /// <see cref="CapturePaymentAsync"/> separately before shipping.
    /// </summary>
    Task<OrderActionResult> MarkPaidManuallyAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Voids the Stripe authorization hold for an order that cannot be fulfilled and
    /// transitions the order to <see cref="OrderStatus.Cancelled"/>. Only valid when
    /// the order is in <c>Paid</c>, <c>InProduction</c>, or <c>ReadyToShip</c> status
    /// (i.e. the payment has been authorised but not yet captured). Sends the customer
    /// a cancellation notice by email. Stripe cancellation failures are logged but do
    /// not block the order from being marked Cancelled in the DB.
    /// </summary>
    Task<OrderActionResult> CancelPaymentAsync(int orderId, CancellationToken ct = default);
}
