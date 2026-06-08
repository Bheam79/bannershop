using System.Globalization;
using System.Net;
using System.Text;
using BannerShop.Api.Models.Orders;
using BannerShop.Api.Services.AiCredits;
using BannerShop.Api.Services.Email;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Api.Services.Shipping;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Services.Orders;

public class OrderService : IOrderService
{
    private const string KeyExpressFee            = "express_fee";
    private const string KeyStandardLeadTimeDays  = "standard_lead_time_days";
    private const string KeyExpressLeadTimeDays   = "express_lead_time_days";
    private const string KeyAiActivationFeeNok    = "ai_banner_activation_fee_nok";
    private const string KeyAiActivationCredits   = "ai_banner_activation_credits";

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
    private readonly IPricingService _pricing;
    private readonly IShippingService _shipping;
    private readonly ParcelCalculator _parcels;
    private readonly IStripePaymentService _stripe;
    private readonly IEmailService _email;
    private readonly IAiCreditService _aiCredits;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        BannerShopDbContext db,
        IPricingService pricing,
        IShippingService shipping,
        ParcelCalculator parcels,
        IStripePaymentService stripe,
        IEmailService email,
        IAiCreditService aiCredits,
        ILogger<OrderService> logger)
    {
        _db = db;
        _pricing = pricing;
        _shipping = shipping;
        _parcels = parcels;
        _stripe = stripe;
        _email = email;
        _aiCredits = aiCredits;
        _logger = logger;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Customer-facing operations
    // ────────────────────────────────────────────────────────────────────────

    public async Task<CreateOrderDraftResult> CreateDraftAsync(
        int userId, CreateOrderDraftRequest req, CancellationToken ct = default)
    {
        if (req.Items is null || req.Items.Count == 0)
            return Fail("Order must contain at least one item.");

        // ── Validate BannerDesignIds (if any) in one round-trip ──
        var requestedDesignIds = req.Items
            .Where(i => i.BannerDesignId.HasValue)
            .Select(i => i.BannerDesignId!.Value)
            .Distinct()
            .ToList();
        if (requestedDesignIds.Count > 0)
        {
            // Accept designs owned by this user OR anonymous designs (UserId == null).
            // Anonymous designs were uploaded before the user registered/logged in;
            // the important security gate is that the user is now authenticated here.
            var ownedDesigns = await _db.BannerDesigns
                .AsNoTracking()
                .Where(d => requestedDesignIds.Contains(d.Id)
                         && (d.UserId == null || d.UserId == userId))
                .Select(d => d.Id)
                .ToListAsync(ct);
            foreach (var designId in requestedDesignIds)
            {
                if (!ownedDesigns.Contains(designId))
                    return Fail($"BannerDesign {designId} not found or does not belong to this user.");
            }
        }

        // ── Validate DesignRequestIds (if any) in one round-trip ──
        var requestedDesignRequestIds = req.Items
            .Where(i => i.DesignRequestId.HasValue)
            .Select(i => i.DesignRequestId!.Value)
            .Distinct()
            .ToList();
        if (requestedDesignRequestIds.Count > 0)
        {
            var ownedRequests = await _db.DesignRequests
                .AsNoTracking()
                .Where(r => requestedDesignRequestIds.Contains(r.Id) && r.UserId == userId)
                .Select(r => r.Id)
                .ToListAsync(ct);
            foreach (var drId in requestedDesignRequestIds)
            {
                if (!ownedRequests.Contains(drId))
                    return Fail($"DesignRequest {drId} not found or does not belong to this user.");
            }
        }

        // ── Load all referenced banner sizes (with material) in one round-trip ──
        var sizeIds = req.Items.Select(i => i.BannerSizeId).Distinct().ToList();
        var sizes = await _db.BannerSizes
            .Include(s => s.Material)
            .Where(s => sizeIds.Contains(s.Id) && s.IsActive)
            .ToDictionaryAsync(s => s.Id, ct);

        foreach (var input in req.Items)
        {
            if (!sizes.ContainsKey(input.BannerSizeId))
                return Fail($"Banner size {input.BannerSizeId} not found or inactive.");
            var size = sizes[input.BannerSizeId];
            if (size.IsCustomWidth && input.CustomWidthCm is null)
                return Fail($"Banner size {input.BannerSizeId} requires customWidthCm.");
            if (!size.IsCustomWidth && input.CustomWidthCm is not null)
                return Fail($"Banner size {input.BannerSizeId} is not a custom-width size.");
        }

        // ── Snapshot pricing per item ──
        var items = new List<OrderItem>(req.Items.Count);
        decimal itemsSubtotal = 0m;
        foreach (var input in req.Items)
        {
            var size = sizes[input.BannerSizeId];
            var unitPrice = await _pricing.CalculatePriceAsync(size, input.CustomWidthCm);
            var widthCm = size.IsCustomWidth ? (input.CustomWidthCm ?? 0) : (size.WidthCm ?? 0);
            var areaSqm = decimal.Round((widthCm / 100m) * (size.HeightCm / 100m), 4);

            // Eyelet (malje) addon — calculated server-side so the price is snapshotted
            // against the pricing parameters at order time.
            var (eyeletFee, eyeletCount) = await _pricing.CalculateEyeletCostAsync(
                widthCm, size.HeightCm, input.EyeletOption);

            var lineTotal = decimal.Round((unitPrice + eyeletFee) * input.Quantity, 2);
            itemsSubtotal += lineTotal;

            items.Add(new OrderItem
            {
                BannerSizeId     = size.Id,
                CustomWidthCm    = input.CustomWidthCm,
                HeightCm         = size.HeightCm,
                Quantity         = input.Quantity,
                AreaSqm          = areaSqm,
                UnitPriceNok     = decimal.Round(unitPrice, 2),
                EyeletOption     = input.EyeletOption,
                EyeletCount      = eyeletCount,
                EyeletFeeNok     = decimal.Round(eyeletFee, 2),
                LineTotalNok     = lineTotal,
                Notes            = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
                BannerDesignId   = input.BannerDesignId,
                DesignRequestId  = input.DesignRequestId
            });
        }

        // ── Calculate shipping (sum of per-line parcel quotes) ──
        decimal shippingCost = 0m;
        int maxCarrierDays = 0;
        try
        {
            foreach (var input in req.Items)
            {
                var size = sizes[input.BannerSizeId];
                var parcel = await _parcels.CalculateAsync(size, input.CustomWidthCm, input.Quantity, ct);
                var quote = await _shipping.CalculateAsync(req.ShippingAddress.PostalCode, req.ShippingAddress.City, parcel, ct);
                shippingCost += quote.Standard.CostNok;
                if (quote.Standard.EstimatedDays > maxCarrierDays)
                    maxCarrierDays = quote.Standard.EstimatedDays;
            }
        }
        catch (ShippingUnavailableException ex)
        {
            return Fail($"Shipping cost unavailable: {ex.Message}");
        }

        // ── Express fee + AI activation fee + lead-time params ──
        var pricingParams = await _db.PricingParameters
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        var expressFee = req.DeliveryType == DeliveryType.Express
            ? pricingParams.GetValueOrDefault(KeyExpressFee, 500m)
            : 0m;
        var productionLeadDays = req.DeliveryType == DeliveryType.Express
            ? (int)pricingParams.GetValueOrDefault(KeyExpressLeadTimeDays, 3m)
            : (int)pricingParams.GetValueOrDefault(KeyStandardLeadTimeDays, 14m);

        // AI activation fee: charged once per order when any item includes a DesignRequest.
        var hasAiDesign = items.Any(i => i.DesignRequestId.HasValue);
        var aiActivationFee = hasAiDesign
            ? pricingParams.GetValueOrDefault(KeyAiActivationFeeNok, 95m)
            : 0m;

        var total = decimal.Round(itemsSubtotal + shippingCost + expressFee + aiActivationFee, 2);
        var estimatedDelivery = DateTime.UtcNow.Date.AddDays(productionLeadDays + Math.Max(0, maxCarrierDays));

        // ── Persist Address (always create a fresh snapshot row for this order) ──
        var address = new Address
        {
            UserId     = userId,
            Line1      = req.ShippingAddress.Line1.Trim(),
            Line2      = string.IsNullOrWhiteSpace(req.ShippingAddress.Line2) ? null : req.ShippingAddress.Line2.Trim(),
            PostalCode = req.ShippingAddress.PostalCode.Trim(),
            City       = req.ShippingAddress.City.Trim(),
            Country    = string.IsNullOrWhiteSpace(req.ShippingAddress.Country) ? "NO" : req.ShippingAddress.Country.Trim()
        };
        _db.Addresses.Add(address);

        var order = new Order
        {
            UserId              = userId,
            Status              = OrderStatus.PendingPayment,
            DeliveryType        = req.DeliveryType,
            ShippingAddress     = address,
            ShippingCostNok     = decimal.Round(shippingCost, 2),
            ExpressFeeNok       = expressFee,
            AiActivationFeeNok  = aiActivationFee,
            TotalNok            = total,
            CreatedAt           = DateTime.UtcNow,
            UpdatedAt           = DateTime.UtcNow,
            EstimatedDelivery   = estimatedDelivery,
            Items               = items
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        // ── Create PaymentIntent ──
        var intent = await _stripe.CreatePaymentIntentAsync(order.Id, userId, total, ct);
        order.StripePaymentIntentId = intent.PaymentIntentId;
        await _db.SaveChangesAsync(ct);

        return new CreateOrderDraftResult(
            Success: true,
            Error: null,
            OrderId: order.Id,
            ClientSecret: intent.ClientSecret,
            TotalNok: total,
            Breakdown: new OrderPriceBreakdownDto
            {
                ItemsSubtotalNok   = decimal.Round(itemsSubtotal, 2),
                ShippingCostNok    = decimal.Round(shippingCost, 2),
                ExpressFeeNok      = expressFee,
                AiActivationFeeNok = aiActivationFee,
                TotalNok           = total
            });
    }

    public async Task<PagedResult<OrderListItemDto>> ListMineAsync(int userId, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _db.Orders.AsNoTracking().Where(o => o.UserId == userId);
        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new OrderListItemDto
            {
                Id = o.Id,
                Status = o.Status.ToString(),
                DeliveryType = o.DeliveryType.ToString(),
                TotalNok = o.TotalNok,
                ItemCount = o.Items.Count,
                CreatedAt = o.CreatedAt,
                EstimatedDelivery = o.EstimatedDelivery,
                CustomerName = o.User.Name,
                CustomerEmail = o.User.Email
            })
            .ToListAsync(ct);

        return new PagedResult<OrderListItemDto>
        {
            Items = rows, Page = page, PageSize = pageSize, TotalCount = total
        };
    }

    public async Task<OrderDetailDto?> GetMineAsync(int userId, int orderId, CancellationToken ct = default)
    {
        var o = await LoadFullOrderAsync(orderId, ct);
        if (o is null || o.UserId != userId) return null;
        return ToDetailDto(o);
    }

    public async Task<OrderActionResult> CancelMineAsync(int userId, int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || order.UserId != userId)
            return OrderActionResult.Fail("Order not found.");
        if (order.Status is not (OrderStatus.Draft or OrderStatus.PendingPayment))
            return OrderActionResult.Fail($"Order in status {order.Status} cannot be cancelled by the customer.");

        order.Status = OrderStatus.Cancelled;
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(order.StripePaymentIntentId))
            await _stripe.CancelPaymentIntentAsync(order.StripePaymentIntentId, ct);

        var full = await LoadFullOrderAsync(orderId, ct);
        return OrderActionResult.Ok(ToDetailDto(full!));
    }

    // ────────────────────────────────────────────────────────────────────────
    // Admin operations
    // ────────────────────────────────────────────────────────────────────────

    public async Task<PagedResult<OrderListItemDto>> ListAllAsync(AdminOrderFilter filter, CancellationToken ct = default)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 || filter.PageSize > 200 ? 20 : filter.PageSize;

        var query = _db.Orders.AsNoTracking().AsQueryable();
        if (filter.Status is { } s)
            query = query.Where(o => o.Status == s);
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
        var rows = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new OrderListItemDto
            {
                Id = o.Id,
                Status = o.Status.ToString(),
                DeliveryType = o.DeliveryType.ToString(),
                TotalNok = o.TotalNok,
                ItemCount = o.Items.Count,
                CreatedAt = o.CreatedAt,
                EstimatedDelivery = o.EstimatedDelivery,
                CustomerName = o.User.Name,
                CustomerEmail = o.User.Email
            })
            .ToListAsync(ct);

        return new PagedResult<OrderListItemDto>
        {
            Items = rows, Page = page, PageSize = pageSize, TotalCount = total
        };
    }

    public async Task<OrderDetailDto?> GetAnyAsync(int orderId, CancellationToken ct = default)
    {
        var o = await LoadFullOrderAsync(orderId, ct);
        return o is null ? null : ToDetailDto(o);
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
        order.UpdatedAt = DateTime.UtcNow;

        // Auto-stamp DeliveredAt when transitioning to Delivered
        if (status == OrderStatus.Delivered)
        {
            var tracking = await _db.ShipmentTrackings.FirstOrDefaultAsync(t => t.OrderId == orderId, ct);
            if (tracking is not null && tracking.DeliveredAt is null)
                tracking.DeliveredAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        var full = await LoadFullOrderAsync(orderId, ct);
        return OrderActionResult.Ok(ToDetailDto(full!));
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
                order.Status = OrderStatus.InProduction;

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
        var full = await LoadFullOrderAsync(orderId, ct);
        return OrderActionResult.Ok(ToDetailDto(full!));
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
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var full = await LoadFullOrderAsync(orderId, ct);
        if (full is not null)
            await TrySendShipmentDispatchedAsync(full, ct);
        return OrderActionResult.Ok(ToDetailDto(full!));
    }

    // ────────────────────────────────────────────────────────────────────────
    // Webhook hooks
    // ────────────────────────────────────────────────────────────────────────

    public async Task MarkPaidAsync(string paymentIntentId, int? orderIdHint, CancellationToken ct = default)
    {
        var order = await ResolveOrderForWebhook(paymentIntentId, orderIdHint, ct);
        if (order is null)
        {
            _logger.LogWarning("Stripe succeeded webhook for unknown PI {Pi} (hint orderId={Hint})", paymentIntentId, orderIdHint);
            return;
        }
        if (order.Status is OrderStatus.Paid or OrderStatus.InProduction or OrderStatus.ReadyToShip
                          or OrderStatus.Shipped or OrderStatus.Delivered)
        {
            // Idempotent: webhook may fire multiple times
            return;
        }

        order.Status = OrderStatus.Paid;
        order.UpdatedAt = DateTime.UtcNow;

        // Seed initial Queued production rows for each item
        var items = await _db.OrderItems
            .Where(i => i.OrderId == order.Id)
            .Include(i => i.ProductionStatuses)
            .ToListAsync(ct);
        foreach (var item in items)
        {
            if (!item.ProductionStatuses.Any())
            {
                item.ProductionStatuses.Add(new ProductionStatus
                {
                    Stage = ProductionStage.Queued,
                    UpdatedAt = DateTime.UtcNow,
                    Notes = "Auto-created on payment received"
                });
            }
        }

        await _db.SaveChangesAsync(ct);

        // ── Grant AI credits when the order includes an AI activation fee ────────
        // Idempotent: GrantAsync uses referenceId = "order:{orderId}" so a second
        // webhook fire will be a no-op (BANNERSH-68).
        if (order.AiActivationFeeNok > 0m && order.UserId > 0)
        {
            try
            {
                var pricingParams = await _db.PricingParameters
                    .AsNoTracking()
                    .Where(p => p.Key == KeyAiActivationCredits)
                    .FirstOrDefaultAsync(ct);
                var creditCount = pricingParams is not null ? (int)pricingParams.Value : 20;
                var referenceId = $"order:{order.Id}";

                await _aiCredits.GrantAsync(
                    order.UserId,
                    count: creditCount,
                    reason: CreditReason.BannerOrderActivation,
                    referenceId: referenceId,
                    ct: ct);

                _logger.LogInformation(
                    "Granted {Count} AI credits to user {UserId} for order {OrderId} (ref={Ref}).",
                    creditCount, order.UserId, order.Id, referenceId);
            }
            catch (Exception ex)
            {
                // Credit grant failures must never block the payment confirmation flow.
                _logger.LogError(ex,
                    "Failed to grant AI activation credits for order {OrderId}.", order.Id);
            }
        }

        // Fire-and-forget order-confirmation email — failures must never bubble
        // up to the Stripe webhook caller, which would otherwise retry the
        // whole MarkPaid flow on every send error.
        var full = await LoadFullOrderAsync(order.Id, ct);
        if (full is not null)
            await TrySendOrderConfirmationAsync(full, ct);
    }

    public async Task MarkPaymentFailedAsync(string paymentIntentId, int? orderIdHint, string? failureMessage, CancellationToken ct = default)
    {
        var order = await ResolveOrderForWebhook(paymentIntentId, orderIdHint, ct);
        if (order is null) return;

        // Keep the order at PendingPayment so the customer can retry; just log a note.
        _logger.LogWarning("Payment failed for order {OrderId} (PI {Pi}): {Msg}", order.Id, paymentIntentId, failureMessage);
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    private async Task<Order?> ResolveOrderForWebhook(string paymentIntentId, int? orderIdHint, CancellationToken ct)
    {
        Order? order = null;
        if (!string.IsNullOrEmpty(paymentIntentId))
            order = await _db.Orders.FirstOrDefaultAsync(o => o.StripePaymentIntentId == paymentIntentId, ct);
        if (order is null && orderIdHint is int id)
            order = await _db.Orders.FindAsync(new object?[] { id }, ct);
        return order;
    }

    private Task<Order?> LoadFullOrderAsync(int orderId, CancellationToken ct)
        => _db.Orders
            .Include(o => o.User)
            .Include(o => o.ShippingAddress)
            .Include(o => o.ShipmentTracking)
            .Include(o => o.Items).ThenInclude(i => i.BannerSize)
            .Include(o => o.Items).ThenInclude(i => i.ProductionStatuses)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

    private static OrderDetailDto ToDetailDto(Order o) => new()
    {
        Id = o.Id,
        UserId = o.UserId,
        CustomerName = o.User?.Name,
        CustomerEmail = o.User?.Email,
        Status = o.Status.ToString(),
        DeliveryType = o.DeliveryType.ToString(),
        ShippingCostNok = o.ShippingCostNok,
        ExpressFeeNok = o.ExpressFeeNok,
        AiActivationFeeNok = o.AiActivationFeeNok,
        TotalNok = o.TotalNok,
        StripePaymentIntentId = o.StripePaymentIntentId,
        CreatedAt = o.CreatedAt,
        UpdatedAt = o.UpdatedAt,
        EstimatedDelivery = o.EstimatedDelivery,
        ShippingAddress = o.ShippingAddress is null ? null : new OrderAddressDto
        {
            Line1 = o.ShippingAddress.Line1,
            Line2 = o.ShippingAddress.Line2,
            PostalCode = o.ShippingAddress.PostalCode,
            City = o.ShippingAddress.City,
            Country = o.ShippingAddress.Country
        },
        Items = o.Items.OrderBy(i => i.Id).Select(i => new OrderItemDto
        {
            Id = i.Id,
            BannerSizeId = i.BannerSizeId,
            BannerSizeName = i.BannerSize?.Name,
            CustomWidthCm = i.CustomWidthCm,
            HeightCm = i.HeightCm,
            Quantity = i.Quantity,
            AreaSqm = i.AreaSqm,
            UnitPriceNok = i.UnitPriceNok,
            EyeletOption = i.EyeletOption.ToString(),
            EyeletCount = i.EyeletCount,
            EyeletFeeNok = i.EyeletFeeNok,
            LineTotalNok = i.LineTotalNok,
            Notes = i.Notes,
            BannerDesignId = i.BannerDesignId,
            DesignRequestId = i.DesignRequestId,
            CurrentProductionStage = (i.ProductionStatuses.OrderByDescending(p => p.UpdatedAt).FirstOrDefault()?.Stage
                                      ?? ProductionStage.Queued).ToString(),
            ProductionStatusHistory = i.ProductionStatuses
                .OrderBy(p => p.UpdatedAt)
                .Select(p => new ProductionStatusDto
                {
                    Id = p.Id,
                    Stage = p.Stage.ToString(),
                    UpdatedAt = p.UpdatedAt,
                    Notes = p.Notes
                }).ToList()
        }).ToList(),
        ShipmentTracking = o.ShipmentTracking is null ? null : new ShipmentTrackingDto
        {
            Carrier = o.ShipmentTracking.Carrier,
            TrackingNumber = o.ShipmentTracking.TrackingNumber,
            TrackingUrl = o.ShipmentTracking.TrackingUrl,
            ShippedAt = o.ShipmentTracking.ShippedAt,
            EstimatedArrival = o.ShipmentTracking.EstimatedArrival,
            DeliveredAt = o.ShipmentTracking.DeliveredAt
        }
    };

    private static CreateOrderDraftResult Fail(string error) => new(
        Success: false,
        Error: error,
        OrderId: 0,
        ClientSecret: string.Empty,
        TotalNok: 0m,
        Breakdown: new OrderPriceBreakdownDto());

    // ────────────────────────────────────────────────────────────────────────
    // Transactional email — fire-and-forget wrappers + body builders.
    // Email failures must NEVER propagate to the caller (Stripe webhook or
    // admin endpoint); they are logged and swallowed.
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Norwegian (nb-NO) culture for currency/date formatting in customer mail.</summary>
    private static readonly CultureInfo NoCulture = CultureInfo.GetCultureInfo("nb-NO");

    private async Task TrySendOrderConfirmationAsync(Order order, CancellationToken ct)
    {
        var to = order.User?.Email;
        if (string.IsNullOrWhiteSpace(to))
        {
            _logger.LogWarning("Skipping order-confirmation email for order {OrderId}: no recipient email on user.", order.Id);
            return;
        }

        try
        {
            var subject = $"Ordrebekreftelse – BannerShop #{order.Id}";
            var body = BuildOrderConfirmationHtml(order);
            await _email.SendAsync(to, subject, body, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send order-confirmation email for order {OrderId} to {To}", order.Id, to);
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
            var body = BuildShipmentDispatchedHtml(order);
            await _email.SendAsync(to, subject, body, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send shipment-dispatched email for order {OrderId} to {To}", order.Id, to);
        }
    }

    private static string BuildOrderConfirmationHtml(Order o)
    {
        var customerName = string.IsNullOrWhiteSpace(o.User?.Name) ? "kunde" : o.User!.Name;
        var itemsSubtotal = o.Items.Sum(i => i.LineTotalNok);
        var estimatedDelivery = o.EstimatedDelivery.HasValue
            ? o.EstimatedDelivery.Value.ToString("d. MMMM yyyy", NoCulture)
            : "ikke fastsatt";

        var sb = new StringBuilder();
        sb.Append("<html><body style=\"font-family:Arial,Helvetica,sans-serif;color:#222;\">");
        sb.Append($"<p>Hei {Esc(customerName)},</p>");
        sb.Append($"<p>Takk for bestillingen din! Vi har mottatt betaling for ordre <strong>#{o.Id}</strong>.</p>");
        sb.Append("<h3>Bestilte varer</h3>");
        sb.Append("<table cellpadding=\"6\" cellspacing=\"0\" border=\"1\" style=\"border-collapse:collapse;border-color:#ccc;\">");
        sb.Append("<thead><tr style=\"background:#f5f5f5;text-align:left;\">");
        sb.Append("<th>Bannerstørrelse</th><th>Antall</th><th>Enhetspris</th><th>Sum</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var item in o.Items.OrderBy(i => i.Id))
        {
            var sizeName = item.BannerSize?.Name ?? $"Bannerstørrelse {item.BannerSizeId}";
            var widthCm = item.CustomWidthCm ?? item.BannerSize?.WidthCm;
            var dims = widthCm.HasValue
                ? $"{widthCm}×{item.HeightCm} cm"
                : $"{item.HeightCm} cm høyde";
            sb.Append("<tr>");
            sb.Append($"<td>{Esc(sizeName)} <span style=\"color:#666;\">({dims})</span></td>");
            sb.Append($"<td>{item.Quantity}</td>");
            sb.Append($"<td>{FormatNok(item.UnitPriceNok)}</td>");
            sb.Append($"<td>{FormatNok(item.LineTotalNok)}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");

        sb.Append("<h3>Sammendrag</h3>");
        sb.Append("<table cellpadding=\"4\" cellspacing=\"0\" border=\"0\">");
        sb.Append($"<tr><td>Delsum varer</td><td style=\"text-align:right;\">{FormatNok(itemsSubtotal)}</td></tr>");
        sb.Append($"<tr><td>Frakt</td><td style=\"text-align:right;\">{FormatNok(o.ShippingCostNok)}</td></tr>");
        if (o.ExpressFeeNok > 0m)
            sb.Append($"<tr><td>Ekspressgebyr</td><td style=\"text-align:right;\">{FormatNok(o.ExpressFeeNok)}</td></tr>");
        if (o.AiActivationFeeNok > 0m)
            sb.Append($"<tr><td>AI aktivering</td><td style=\"text-align:right;\">{FormatNok(o.AiActivationFeeNok)}</td></tr>");
        sb.Append($"<tr><td><strong>Totalsum</strong></td><td style=\"text-align:right;\"><strong>{FormatNok(o.TotalNok)}</strong></td></tr>");
        sb.Append("</table>");

        sb.Append($"<p>Estimert leveringsdato: <strong>{Esc(estimatedDelivery)}</strong>.</p>");
        sb.Append("<p>Vi gir beskjed igjen så snart pakken er sendt fra oss.</p>");
        sb.Append("<p>Vennlig hilsen,<br/>BannerShop</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string BuildShipmentDispatchedHtml(Order o)
    {
        var customerName = string.IsNullOrWhiteSpace(o.User?.Name) ? "kunde" : o.User!.Name;
        var t = o.ShipmentTracking!;
        var arrival = t.EstimatedArrival?.ToString("d. MMMM yyyy", NoCulture)
                      ?? o.EstimatedDelivery?.ToString("d. MMMM yyyy", NoCulture)
                      ?? "ikke fastsatt";

        var sb = new StringBuilder();
        sb.Append("<html><body style=\"font-family:Arial,Helvetica,sans-serif;color:#222;\">");
        sb.Append($"<p>Hei {Esc(customerName)},</p>");
        sb.Append($"<p>Gode nyheter — ordre <strong>#{o.Id}</strong> er nå sendt fra oss.</p>");
        sb.Append("<h3>Sporing</h3>");
        sb.Append("<table cellpadding=\"4\" cellspacing=\"0\" border=\"0\">");
        sb.Append($"<tr><td>Transportør</td><td><strong>{Esc(t.Carrier)}</strong></td></tr>");
        sb.Append($"<tr><td>Sporingsnummer</td><td><strong>{Esc(t.TrackingNumber)}</strong></td></tr>");
        if (!string.IsNullOrWhiteSpace(t.TrackingUrl))
            sb.Append($"<tr><td>Sporing</td><td><a href=\"{Esc(t.TrackingUrl)}\">Følg pakken</a></td></tr>");
        sb.Append($"<tr><td>Estimert ankomst</td><td>{Esc(arrival)}</td></tr>");
        sb.Append("</table>");
        sb.Append("<p>Takk for at du handlet hos oss!</p>");
        sb.Append("<p>Vennlig hilsen,<br/>BannerShop</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string FormatNok(decimal amount)
        => string.Format(NoCulture, "{0:N2} kr", amount);

    private static string Esc(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
}
