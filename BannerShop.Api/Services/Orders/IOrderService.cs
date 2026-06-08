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

    // ── Admin operations ──────────────────────────────────────────────────────

    Task<PagedResult<OrderListItemDto>> ListAllAsync(AdminOrderFilter filter, CancellationToken ct = default);

    Task<OrderDetailDto?> GetAnyAsync(int orderId, CancellationToken ct = default);

    Task<OrderActionResult> UpdateStatusAsync(int orderId, OrderStatus status, CancellationToken ct = default);

    Task<OrderActionResult> UpdateProductionAsync(int orderId, int itemId, ProductionStage stage, string? notes, CancellationToken ct = default);

    Task<OrderActionResult> SetShippingAsync(int orderId, SetShippingRequest req, CancellationToken ct = default);

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
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    /// <summary>Free-text search across order id, customer email and name.</summary>
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
