using BannerShop.Api.Models.Orders;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.Email;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Core.Helpers;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Services.Orders;

/// <summary>
/// Admin-only order operations (BANNERSH-199 split from <see cref="OrderService"/>).
/// </summary>
public class AdminOrderService : IAdminOrderService
{
    /// <summary>
    /// Defines which status transitions an admin is permitted to make.
    /// Delivered and Cancelled are final states — no further transitions are allowed.
    /// </summary>
    private static readonly IReadOnlyDictionary<OrderStatus, IReadOnlySet<OrderStatus>> AllowedTransitions =
        new Dictionary<OrderStatus, IReadOnlySet<OrderStatus>>
        {
            [OrderStatus.Draft]          = new HashSet<OrderStatus> { OrderStatus.PendingPayment, OrderStatus.Paid, OrderStatus.Cancelled },
            [OrderStatus.PendingPayment] = new HashSet<OrderStatus> { OrderStatus.Paid, OrderStatus.Cancelled },
            [OrderStatus.Paid]           = new HashSet<OrderStatus> { OrderStatus.InProduction, OrderStatus.Cancelled },
            [OrderStatus.InProduction]   = new HashSet<OrderStatus> { OrderStatus.ReadyToShip, OrderStatus.Cancelled },
            [OrderStatus.ReadyToShip]    = new HashSet<OrderStatus> { OrderStatus.Shipped, OrderStatus.InProduction, OrderStatus.Cancelled },
            [OrderStatus.Shipped]        = new HashSet<OrderStatus> { OrderStatus.Delivered, OrderStatus.Cancelled },
            [OrderStatus.Delivered]      = new HashSet<OrderStatus>(),
            [OrderStatus.Cancelled]      = new HashSet<OrderStatus>(),
        };

    /// <summary>
    /// Order must be in one of these states before shipping details can be recorded.
    /// </summary>
    private static readonly IReadOnlySet<OrderStatus> ShippableStatuses =
        new HashSet<OrderStatus> { OrderStatus.InProduction, OrderStatus.ReadyToShip };

    private readonly BannerShopDbContext _db;
    private readonly IEmailService _email;
    private readonly BannerFileStorage _storage;
    private readonly IStripePaymentService _stripe;
    private readonly ILogger<AdminOrderService> _logger;

    public AdminOrderService(
        BannerShopDbContext db,
        IEmailService email,
        BannerFileStorage storage,
        IStripePaymentService stripe,
        ILogger<AdminOrderService> logger)
    {
        _db = db;
        _email = email;
        _storage = storage;
        _stripe = stripe;
        _logger = logger;
    }

    public async Task<PagedResult<OrderListItemDto>> ListAllAsync(AdminOrderFilter filter, CancellationToken ct = default)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 || filter.PageSize > 200 ? 20 : filter.PageSize;

        // BANNERSH-185: hide soft-deleted orders from the admin list. (No admin-side
        // "show deleted" toggle yet — they're truly gone from the UI; rows remain in
        // the DB for audit / undo.)
        var query = _db.Orders.AsNoTracking().Where(o => !o.Deleted).AsQueryable();
        if (filter.Status is { } s)
            query = query.Where(o => o.Status == s);
        if (filter.OrderType is { } t)
            query = query.Where(o => o.OrderType == t);
        else if (!filter.IncludeCreditPacks)
            // BANNERSH-139: hide credit-pack orders from the default admin list so the
            // production team isn't distracted by them. The opt-in flag (or an explicit
            // OrderType=CreditPack filter) bypasses this.
            query = query.Where(o => o.OrderType != OrderType.CreditPack);
        // BANNERSH-169: hide free-first AI design-tracking orders (0 kr, no items) unless
        // the caller opts in. These are created by the AI pipeline to track design-request
        // progress but have no physical production value.
        if (filter.ExcludeZeroValueAiOrders && filter.OrderType is null)
            query = query.Where(o => o.OrderType != OrderType.AiBanner || o.TotalNok > 0);
        if (filter.FromUtc is { } from)
            query = query.Where(o => o.CreatedAt >= from);
        if (filter.ToUtc is { } to)
            query = query.Where(o => o.CreatedAt <= to);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var q = filter.Search.Trim();
            query = int.TryParse(q, out var maybeId)
                ? query.Where(o => o.Id == maybeId
                    || EF.Functions.Like(o.User.Email, $"%{q}%")
                    || EF.Functions.Like(o.User.Name, $"%{q}%"))
                : query.Where(o => EF.Functions.Like(o.User.Email, $"%{q}%")
                    || EF.Functions.Like(o.User.Name, $"%{q}%"));
        }

        var total = await query.CountAsync(ct);
        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Include(o => o.User)
            .Include(o => o.Items).ThenInclude(i => i.BannerSize).ThenInclude(s => s!.Material)
            .Include(o => o.Items).ThenInclude(i => i.BannerDesign)
            .AsSplitQuery()
            .ToListAsync(ct);

        var drs = await OrderQueries.LoadDesignRequestsForOrdersAsync(_db, orders.Select(o => o.Id).ToList(), ct);
        var rows = orders.Select(o => OrderMapper.ToListItemDto(o, drs.GetValueOrDefault(o.Id), _storage)).ToList();

        return new PagedResult<OrderListItemDto>
        {
            Items = rows, Page = page, PageSize = pageSize, TotalCount = total
        };
    }

    public async Task<OrderDetailDto?> GetAnyAsync(int orderId, CancellationToken ct = default)
    {
        var o = await OrderQueries.LoadFullOrderAsync(_db, orderId, ct);
        if (o is null || o.Deleted) return null;
        var dr = await OrderQueries.LoadDesignRequestForOrderAsync(_db, orderId, ct);
        return OrderMapper.ToDetailDto(o, dr, _storage);
    }

    public async Task<OrderActionResult> UpdateStatusAsync(int orderId, OrderStatus status, CancellationToken ct = default)
    {
        var order = await _db.Orders.FindAsync(new object?[] { orderId }, ct);
        if (order is null) return OrderActionResult.Fail("Order not found.");

        if (!AllowedTransitions.TryGetValue(order.Status, out var allowed) || !allowed.Contains(status))
        {
            return OrderActionResult.FailTransition(
                $"Cannot transition order from '{order.Status}' to '{status}'. " +
                $"Allowed targets from '{order.Status}': " +
                (allowed is { Count: > 0 }
                    ? string.Join(", ", allowed)
                    : "none (final state)") + ".");
        }

        order.Status = status;
        // Mirror onto OrderState for status values that have a direct mapping.
        order.OrderState = status switch
        {
            OrderStatus.Paid          => OrderState.Paid,
            OrderStatus.InProduction  => OrderState.InProduction,
            OrderStatus.Shipped       => OrderState.Shipped,
            OrderStatus.Delivered     => OrderState.Delivered,
            OrderStatus.Cancelled     => OrderState.Cancelled,
            _                         => order.OrderState // keep existing for Draft / PendingPayment / ReadyToShip
        };
        order.UpdatedAt = DateTime.UtcNow;

        // Auto-stamp DeliveredAt when transitioning to Delivered
        if (status == OrderStatus.Delivered)
        {
            var tracking = await _db.ShipmentTrackings.FirstOrDefaultAsync(t => t.OrderId == orderId, ct);
            if (tracking is not null && tracking.DeliveredAt is null)
                tracking.DeliveredAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        var full = await OrderQueries.LoadFullOrderAsync(_db, orderId, ct);
        var drStatus = await OrderQueries.LoadDesignRequestForOrderAsync(_db, orderId, ct);
        return OrderActionResult.Ok(OrderMapper.ToDetailDto(full!, drStatus, _storage));
    }

    public async Task<OrderActionResult> UpdateProductionAsync(int orderId, int itemId, ProductionStage stage, string? notes, CancellationToken ct = default)
    {
        var item = await _db.OrderItems
            .Include(i => i.ProductionStatuses)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.OrderId == orderId, ct);
        if (item is null) return OrderActionResult.Fail("Order item not found.");

        item.ProductionStatuses.Add(new ProductionStatus
        {
            Stage = stage,
            UpdatedAt = DateTime.UtcNow,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        });

        // Promote order status to InProduction when first item starts moving forward
        var order = await _db.Orders.FindAsync(new object?[] { orderId }, ct);
        if (order is not null)
        {
            if (order.Status == OrderStatus.Paid && stage != ProductionStage.Queued)
            {
                order.Status = OrderStatus.InProduction;
                order.OrderState = OrderState.InProduction;
            }

            // If ALL items report ReadyToShip, transition the whole order
            if (stage == ProductionStage.ReadyToShip)
            {
                var allItems = await _db.OrderItems
                    .Where(i => i.OrderId == orderId)
                    .Include(i => i.ProductionStatuses)
                    .ToListAsync(ct);
                bool everyItemReady = allItems.All(i =>
                {
                    var latest = i.ProductionStatuses.OrderByDescending(p => p.UpdatedAt).FirstOrDefault();
                    return latest?.Stage == ProductionStage.ReadyToShip;
                });
                if (everyItemReady && order.Status != OrderStatus.Shipped && order.Status != OrderStatus.Delivered)
                    order.Status = OrderStatus.ReadyToShip;
            }
            order.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        var full = await OrderQueries.LoadFullOrderAsync(_db, orderId, ct);
        var drProd = await OrderQueries.LoadDesignRequestForOrderAsync(_db, orderId, ct);
        return OrderActionResult.Ok(OrderMapper.ToDetailDto(full!, drProd, _storage));
    }

    public async Task<OrderActionResult> SetShippingAsync(int orderId, SetShippingRequest req, CancellationToken ct = default)
    {
        var order = await _db.Orders
            .Include(o => o.ShipmentTracking)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return OrderActionResult.Fail("Order not found.");

        if (!ShippableStatuses.Contains(order.Status))
        {
            return OrderActionResult.FailTransition(
                $"Order is in status '{order.Status}' and cannot be shipped. " +
                $"Order must be in one of: {string.Join(", ", ShippableStatuses)} before recording shipment details.");
        }

        if (order.ShipmentTracking is null)
        {
            order.ShipmentTracking = new ShipmentTracking
            {
                OrderId = order.Id,
                Carrier = req.Carrier.Trim(),
                TrackingNumber = req.TrackingNumber.Trim(),
                TrackingUrl = string.IsNullOrWhiteSpace(req.TrackingUrl) ? null : req.TrackingUrl.Trim(),
                ShippedAt = req.ShippedAt ?? DateTime.UtcNow,
                EstimatedArrival = req.EstimatedArrival
            };
        }
        else
        {
            var t = order.ShipmentTracking;
            t.Carrier = req.Carrier.Trim();
            t.TrackingNumber = req.TrackingNumber.Trim();
            t.TrackingUrl = string.IsNullOrWhiteSpace(req.TrackingUrl) ? null : req.TrackingUrl.Trim();
            t.ShippedAt = req.ShippedAt ?? t.ShippedAt ?? DateTime.UtcNow;
            t.EstimatedArrival = req.EstimatedArrival ?? t.EstimatedArrival;
        }

        order.Status = OrderStatus.Shipped;
        order.OrderState = OrderState.Shipped;
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Capture the Stripe authorization hold now that the item is shipped.
        // Failures are logged but must not block the shipment record being saved.
        if (!string.IsNullOrWhiteSpace(order.StripePaymentIntentId))
        {
            try
            {
                await _stripe.CapturePaymentIntentAsync(order.StripePaymentIntentId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to capture Stripe PI {Pi} for order {OrderId}. " +
                    "Capture manually in the Stripe Dashboard to collect payment.",
                    order.StripePaymentIntentId, orderId);
            }
        }

        var full = await OrderQueries.LoadFullOrderAsync(_db, orderId, ct);
        if (full is not null)
            await TrySendShipmentDispatchedAsync(full, ct);
        var drShip = await OrderQueries.LoadDesignRequestForOrderAsync(_db, orderId, ct);
        return OrderActionResult.Ok(OrderMapper.ToDetailDto(full!, drShip, _storage));
    }

    /// <inheritdoc />
    public async Task<OrderActionResult> AdvanceStateAsync(
        int orderId, OrderState next, CancellationToken ct = default)
    {
        var order = await _db.Orders.FindAsync(new object?[] { orderId }, ct);
        if (order is null) return OrderActionResult.Fail("Order not found.");

        if (!OrderStateHelper.IsValidTransition(order.OrderType, order.OrderState, next))
        {
            var seq = string.Join(" → ", OrderStateHelper.ValidSequence(order.OrderType)
                .Select(s => s.ToString()));
            return OrderActionResult.FailTransition(
                $"Cannot advance order {orderId} from '{order.OrderState}' to '{next}' " +
                $"for type '{order.OrderType}'. Valid sequence: {seq}.");
        }

        order.OrderState = next;
        // Keep the legacy Status in sync for states that have a direct mapping.
        order.Status = next switch
        {
            OrderState.Paid         => OrderStatus.Paid,
            OrderState.InProduction => OrderStatus.InProduction,
            OrderState.Shipped      => OrderStatus.Shipped,
            OrderState.Delivered    => OrderStatus.Delivered,
            OrderState.Cancelled    => OrderStatus.Cancelled,
            _                       => order.Status
        };
        order.UpdatedAt = DateTime.UtcNow;

        if (next == OrderState.Delivered)
        {
            var tracking = await _db.ShipmentTrackings
                .FirstOrDefaultAsync(t => t.OrderId == orderId, ct);
            if (tracking is not null && tracking.DeliveredAt is null)
                tracking.DeliveredAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        var full = await OrderQueries.LoadFullOrderAsync(_db, orderId, ct);
        if (full is not null && next == OrderState.InProduction)
            await TrySendProductionStartedAsync(full, ct);
        var drAdvance = await OrderQueries.LoadDesignRequestForOrderAsync(_db, orderId, ct);
        return OrderActionResult.Ok(OrderMapper.ToDetailDto(full!, drAdvance, _storage));
    }

    /// <inheritdoc />
    public async Task<OrderActionResult> CapturePaymentAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.FindAsync(new object?[] { orderId }, ct);
        if (order is null) return OrderActionResult.Fail("Order not found.");

        if (string.IsNullOrWhiteSpace(order.StripePaymentIntentId))
            return OrderActionResult.Fail("This order has no Stripe PaymentIntent to capture.");

        // Capture is valid on orders that have been authorised but not yet settled.
        // Allow InProduction / ReadyToShip too so admins can capture if they forgot.
        if (order.Status is not (OrderStatus.Paid or OrderStatus.InProduction or OrderStatus.ReadyToShip))
        {
            return OrderActionResult.FailTransition(
                $"Order is in '{order.Status}' status — only Paid / InProduction / ReadyToShip orders can be captured.");
        }

        try
        {
            await _stripe.CapturePaymentIntentAsync(order.StripePaymentIntentId, ct);
            _logger.LogInformation(
                "Admin manually captured Stripe PI {Pi} for order {OrderId}.",
                order.StripePaymentIntentId, orderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Admin capture of Stripe PI {Pi} for order {OrderId} failed.",
                order.StripePaymentIntentId, orderId);
            return OrderActionResult.Fail($"Stripe capture failed: {ex.Message}");
        }

        var full = await OrderQueries.LoadFullOrderAsync(_db, orderId, ct);
        var dr = await OrderQueries.LoadDesignRequestForOrderAsync(_db, orderId, ct);
        return OrderActionResult.Ok(OrderMapper.ToDetailDto(full!, dr, _storage));
    }

    // ────────────────────────────────────────────────────────────────────────
    // Transactional email — fire-and-forget wrappers.
    // HTML body builders live in OrderEmailTemplates.cs.
    // Email failures must NEVER propagate to the caller (admin endpoint); they
    // are logged and swallowed.
    // ────────────────────────────────────────────────────────────────────────

    private async Task TrySendProductionStartedAsync(Order order, CancellationToken ct)
    {
        var to = order.User?.Email;
        if (string.IsNullOrWhiteSpace(to))
        {
            _logger.LogWarning("Skipping production-started email for order {OrderId}: no recipient email on user.", order.Id);
            return;
        }

        try
        {
            var subject = $"Bestillingen din er sendt til produksjon – BannerShop #{order.Id}";
            var body = OrderEmailTemplates.BuildProductionStartedHtml(order);
            await _email.SendAsync(to, subject, body, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send production-started email for order {OrderId} to {To}", order.Id, to);
        }
    }

    private async Task TrySendShipmentDispatchedAsync(Order order, CancellationToken ct)
    {
        var to = order.User?.Email;
        if (string.IsNullOrWhiteSpace(to))
        {
            _logger.LogWarning("Skipping shipment-dispatched email for order {OrderId}: no recipient email on user.", order.Id);
            return;
        }
        if (order.ShipmentTracking is null)
        {
            _logger.LogWarning("Skipping shipment-dispatched email for order {OrderId}: no ShipmentTracking record.", order.Id);
            return;
        }

        try
        {
            var subject = $"Bestillingen din er sendt – BannerShop #{order.Id}";
            var body = OrderEmailTemplates.BuildShipmentDispatchedHtml(order);
            await _email.SendAsync(to, subject, body, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send shipment-dispatched email for order {OrderId} to {To}", order.Id, to);
        }
    }
}
