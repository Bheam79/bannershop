using BannerShop.Api.Models.Orders;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Api.Services.Shipping;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Services.Orders;

public class OrderService : IOrderService
{
    private const string KeyExpressFee     = "express_fee";
    private const string KeyStandardLeadTimeDays = "standard_lead_time_days";
    private const string KeyExpressLeadTimeDays  = "express_lead_time_days";

    private readonly BannerShopDbContext _db;
    private readonly IPricingService _pricing;
    private readonly IShippingService _shipping;
    private readonly ParcelCalculator _parcels;
    private readonly IStripePaymentService _stripe;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        BannerShopDbContext db,
        IPricingService pricing,
        IShippingService shipping,
        ParcelCalculator parcels,
        IStripePaymentService stripe,
        ILogger<OrderService> logger)
    {
        _db = db;
        _pricing = pricing;
        _shipping = shipping;
        _parcels = parcels;
        _stripe = stripe;
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
            var ownedDesigns = await _db.BannerDesigns
                .AsNoTracking()
                .Where(d => requestedDesignIds.Contains(d.Id) && d.UserId == userId)
                .Select(d => d.Id)
                .ToListAsync(ct);
            foreach (var designId in requestedDesignIds)
            {
                if (!ownedDesigns.Contains(designId))
                    return Fail($"BannerDesign {designId} not found or does not belong to this user.");
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
            var lineTotal = decimal.Round(unitPrice * input.Quantity, 2);
            itemsSubtotal += lineTotal;

            items.Add(new OrderItem
            {
                BannerSizeId   = size.Id,
                CustomWidthCm  = input.CustomWidthCm,
                HeightCm       = size.HeightCm,
                Quantity       = input.Quantity,
                AreaSqm        = areaSqm,
                UnitPriceNok   = decimal.Round(unitPrice, 2),
                LineTotalNok   = lineTotal,
                Notes          = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
                BannerDesignId = input.BannerDesignId
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

        // ── Express fee + lead-time params ──
        var pricingParams = await _db.PricingParameters
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        var expressFee = req.DeliveryType == DeliveryType.Express
            ? pricingParams.GetValueOrDefault(KeyExpressFee, 500m)
            : 0m;
        var productionLeadDays = req.DeliveryType == DeliveryType.Express
            ? (int)pricingParams.GetValueOrDefault(KeyExpressLeadTimeDays, 3m)
            : (int)pricingParams.GetValueOrDefault(KeyStandardLeadTimeDays, 14m);

        var total = decimal.Round(itemsSubtotal + shippingCost + expressFee, 2);
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
                ItemsSubtotalNok = decimal.Round(itemsSubtotal, 2),
                ShippingCostNok  = decimal.Round(shippingCost, 2),
                ExpressFeeNok    = expressFee,
                TotalNok         = total
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
        order.Status = status;
        order.UpdatedAt = DateTime.UtcNow;
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
            LineTotalNok = i.LineTotalNok,
            Notes = i.Notes,
            BannerDesignId = i.BannerDesignId,
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
}
