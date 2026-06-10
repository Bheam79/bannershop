using BannerShop.Api.Models.Orders;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;

namespace BannerShop.Api.Services.Orders;

public interface IOrderService
{
    /// <summary>
    /// Creates a draft order owned by <paramref name="userId"/>, snapshots prices,
    /// calculates shipping, and creates a Stripe PaymentIntent.
    /// </summary>
    Task<CreateOrderDraftResult> CreateDraftAsync(
        int userId,
        CreateOrderDraftRequest req,
        CancellationToken ct = default);

    /// <summary>List orders belonging to a single customer (newest first).</summary>
    Task<PagedResult<OrderListItemDto>> ListMineAsync(int userId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Fetch detail for a single order; returns null when the order does not belong to the caller.</summary>
    Task<OrderDetailDto?> GetMineAsync(int userId, int orderId, CancellationToken ct = default);

    /// <summary>Customer cancels a Draft or PendingPayment order.</summary>
    Task<OrderActionResult> CancelMineAsync(int userId, int orderId, CancellationToken ct = default);

    /// <summary>
    /// BANNERSH-185: customer "Slett" action on a Draft / PendingPayment / Cancelled
    /// order. Soft-deletes the row (sets <c>Deleted=true</c>) so it disappears from
    /// the customer's "Mine ordrer" list, and cancels any open Stripe PaymentIntent.
    /// Paid orders are NOT deletable — accounting records stay visible.
    /// </summary>
    Task<OrderActionResult> DeleteMineAsync(int userId, int orderId, CancellationToken ct = default);

    /// <summary>
    /// BANNERSH-185: customer "Betal nå" / retry action on a PendingPayment order.
    /// Returns a usable client secret — either the existing PaymentIntent's (when it
    /// is still in a retryable state) or a freshly minted one. Already-paid orders
    /// return <c>AlreadyPaid=true</c> with a null client secret so the frontend can
    /// hop straight to the confirmation page.
    /// </summary>
    Task<RetryPaymentResult> RetryPaymentAsync(int userId, int orderId, CancellationToken ct = default);

    /// <summary>
    /// BANNERSH-182: testing-only override. When the operator types the
    /// configured <see cref="TestingOptions.MockPaymentPassword"/> into
    /// the checkout's "Marker som betalt (testmodus)" modal, the order is
    /// flipped to Paid without going through Stripe (so the post-payment
    /// flow — production rows, confirmation email, redirect — can be
    /// exercised end-to-end). Returns a NotFound result when the order
    /// does not belong to the caller or when <c>Testing:EnableMockPayment</c>
    /// is disabled.
    /// </summary>
    Task<OrderActionResult> MockMarkPaidAsync(int userId, int orderId, string password, CancellationToken ct = default);

    /// <summary>
    /// Customer-initiated design approval for an AI or Manual-design order in
    /// <see cref="Core.Enums.OrderState.CustomerApproval"/>. Advances Order state to
    /// <see cref="Core.Enums.OrderState.InProduction"/> and mirrors the approval on the
    /// linked <see cref="Core.Entities.DesignRequest"/> (if any).
    /// </summary>
    Task<OrderActionResult> ApproveDesignAsync(int orderId, int callerUserId, CancellationToken ct = default);

    // ── Internal hook used by the Stripe webhook controller ───────────────────

    Task MarkPaidAsync(string paymentIntentId, int? orderIdHint, CancellationToken ct = default);
    Task MarkPaymentFailedAsync(string paymentIntentId, int? orderIdHint, string? failureMessage, CancellationToken ct = default);
}

public record CreateOrderDraftResult(
    bool Success,
    string? Error,
    int OrderId,
    string ClientSecret,
    decimal TotalNok,
    OrderPriceBreakdownDto Breakdown);

/// <summary>
/// Outcome of <see cref="IOrderService.RetryPaymentAsync"/>. On success carries the
/// client secret to drive Stripe.confirmCardPayment on the frontend (or
/// <c>AlreadyPaid=true</c> with a null secret when the order has already been paid).
/// </summary>
public record RetryPaymentResult(
    bool Success,
    string? Error,
    int OrderId,
    string? ClientSecret,
    decimal TotalNok,
    bool AlreadyPaid)
{
    public static RetryPaymentResult Fail(string error) =>
        new(false, error, 0, null, 0m, false);
}

public enum OrderActionErrorType { NotFound, InvalidTransition }

public record OrderActionResult(bool Success, string? Error, OrderDetailDto? Order = null)
{
    public OrderActionErrorType? ErrorType { get; init; }

    public static OrderActionResult Ok(OrderDetailDto order) => new(true, null, order);
    /// <summary>Order (or order item) was not found.</summary>
    public static OrderActionResult Fail(string error) => new(false, error) { ErrorType = OrderActionErrorType.NotFound };
    /// <summary>The requested status transition is not permitted from the current state.</summary>
    public static OrderActionResult FailTransition(string error) => new(false, error) { ErrorType = OrderActionErrorType.InvalidTransition };
}

public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
}

public class AdminOrderFilter
{
    public OrderStatus? Status { get; init; }
    /// <summary>Filter by order fulfilment type (CustomBanner / AiBanner / ManualDesign / CreditPack).</summary>
    public OrderType? OrderType { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    /// <summary>Free-text search across order id, customer email and name.</summary>
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    /// <summary>
    /// BANNERSH-139: when <c>false</c> (default), <see cref="Core.Enums.OrderType.CreditPack"/>
    /// orders are excluded from the list so the production team isn't distracted by AI
    /// credit-pack purchases when looking for banners to print. Setting <see cref="OrderType"/>
    /// to <c>CreditPack</c> explicitly overrides this filter (the type filter wins).
    /// </summary>
    public bool IncludeCreditPacks { get; init; } = false;

    /// <summary>
    /// BANNERSH-169: when <c>true</c> (default), AI banner orders with <c>TotalNok == 0</c>
    /// are excluded from the admin list. These are free-first design-tracking orders created
    /// by the AI pipeline — they have no order items and no price, so they only confuse the
    /// production team. Set to <c>false</c> (or apply a <see cref="Status"/> filter) to
    /// include them.
    /// </summary>
    public bool ExcludeZeroValueAiOrders { get; init; } = true;
}
