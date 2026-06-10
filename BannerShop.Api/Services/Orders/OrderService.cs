using BannerShop.Api.Models.Orders;
using BannerShop.Api.Services.AiCredits;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.Email;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Api.Services.Shipping;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BannerShop.Api.Services.Orders;

/// <summary>
/// Customer-facing order operations + Stripe webhook hooks. Admin-only operations
/// (status updates, production tracking, shipping, advance-state) live in
/// <see cref="AdminOrderService"/> after the BANNERSH-199 split. DTO mapping was
/// extracted to the static <see cref="OrderMapper"/>.
/// </summary>
public class OrderService : IOrderService
{
    private const string KeyExpressFee            = "express_fee";
    private const string KeyStandardLeadTimeDays  = "standard_lead_time_days";
    private const string KeyExpressLeadTimeDays   = "express_lead_time_days";
    private const string KeyAiActivationFeeNok    = "ai_banner_activation_fee_nok";
    private const string KeyAiActivationCredits   = "ai_banner_activation_credits";

    private readonly BannerShopDbContext _db;
    private readonly IPricingService _pricing;
    private readonly IShippingService _shipping;
    private readonly ParcelCalculator _parcels;
    private readonly IStripePaymentService _stripe;
    private readonly IEmailService _email;
    private readonly IAiCreditService _aiCredits;
    private readonly BannerFileStorage _storage;
    private readonly TestingOptions _testing;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        BannerShopDbContext db,
        IPricingService pricing,
        IShippingService shipping,
        ParcelCalculator parcels,
        IStripePaymentService stripe,
        IEmailService email,
        IAiCreditService aiCredits,
        BannerFileStorage storage,
        IOptions<TestingOptions> testing,
        ILogger<OrderService> logger)
    {
        _db = db;
        _pricing = pricing;
        _shipping = shipping;
        _parcels = parcels;
        _stripe = stripe;
        _email = email;
        _aiCredits = aiCredits;
        _storage = storage;
        _testing = testing.Value;
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

        // Shipping address is required for all delivery types except Pickup
        if (req.DeliveryType != DeliveryType.Pickup && req.ShippingAddress is null)
            return Fail("ShippingAddress is required for Standard and Express delivery types.");

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
        // Pickup orders skip carrier shipping entirely — cost and carrier days are both 0.
        decimal shippingCost = 0m;
        int maxCarrierDays = 0;
        if (req.DeliveryType != DeliveryType.Pickup)
        {
            try
            {
                foreach (var input in req.Items)
                {
                    var size = sizes[input.BannerSizeId];
                    // BANNERSH-143: pass through the customer's packing choice so the
                    // server-side quote matches the price they saw in the cart.
                    var parcel = await _parcels.CalculateAsync(size, input.CustomWidthCm, input.Quantity, req.PackingMode, ct);
                    var quote = await _shipping.CalculateAsync(req.ShippingAddress!.PostalCode, req.ShippingAddress.City, parcel, ct);
                    shippingCost += quote.Standard.CostNok;
                    if (quote.Standard.EstimatedDays > maxCarrierDays)
                        maxCarrierDays = quote.Standard.EstimatedDays;
                }
            }
            catch (ShippingUnavailableException ex)
            {
                return Fail($"Shipping cost unavailable: {ex.Message}");
            }
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
        // Pickup orders have no shipping address — ShippingAddress stays null on the order.
        Address? address = null;
        if (req.DeliveryType != DeliveryType.Pickup && req.ShippingAddress is not null)
        {
            address = new Address
            {
                UserId     = userId,
                Line1      = req.ShippingAddress.Line1.Trim(),
                Line2      = string.IsNullOrWhiteSpace(req.ShippingAddress.Line2) ? null : req.ShippingAddress.Line2.Trim(),
                PostalCode = req.ShippingAddress.PostalCode.Trim(),
                City       = req.ShippingAddress.City.Trim(),
                Country    = string.IsNullOrWhiteSpace(req.ShippingAddress.Country) ? "NO" : req.ShippingAddress.Country.Trim()
            };
            _db.Addresses.Add(address);
        }

        // Determine order type: CustomBanner unless any item references a DesignRequest.
        // AI-design orders route through a separate path (BANNERSH-108); items with a
        // DesignRequestId here are from the banner-builder custom upload flow.
        var orderType = hasAiDesign ? OrderType.AiBanner : OrderType.CustomBanner;

        var order = new Order
        {
            UserId              = userId,
            Status              = OrderStatus.PendingPayment,
            OrderType           = orderType,
            OrderState          = OrderState.Draft,
            DeliveryType        = req.DeliveryType,
            PackingMode         = req.PackingMode,
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

        // BANNERSH-185: exclude soft-deleted orders (Draft / PendingPayment rows the
        // customer cleaned up via the "Slett" button) from the customer's listing.
        var query = _db.Orders.AsNoTracking().Where(o => o.UserId == userId && !o.Deleted);
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

    public async Task<OrderDetailDto?> GetMineAsync(int userId, int orderId, CancellationToken ct = default)
    {
        var o = await OrderQueries.LoadFullOrderAsync(_db, orderId, ct);
        // BANNERSH-185: soft-deleted orders are treated as if they don't exist for the
        // customer — same 404 as a foreign order.
        if (o is null || o.UserId != userId || o.Deleted) return null;
        var dr = await OrderQueries.LoadDesignRequestForOrderAsync(_db, orderId, ct);
        return OrderMapper.ToDetailDto(o, dr, _storage);
    }

    public async Task<OrderActionResult> CancelMineAsync(int userId, int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || order.UserId != userId || order.Deleted)
            return OrderActionResult.Fail("Order not found.");
        if (order.Status is not (OrderStatus.Draft or OrderStatus.PendingPayment))
            return OrderActionResult.Fail($"Order in status {order.Status} cannot be cancelled by the customer.");

        order.Status = OrderStatus.Cancelled;
        order.OrderState = OrderState.Cancelled;
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(order.StripePaymentIntentId))
            await _stripe.CancelPaymentIntentAsync(order.StripePaymentIntentId, ct);

        var full = await OrderQueries.LoadFullOrderAsync(_db, orderId, ct);
        var drCancel = await OrderQueries.LoadDesignRequestForOrderAsync(_db, orderId, ct);
        return OrderActionResult.Ok(OrderMapper.ToDetailDto(full!, drCancel, _storage));
    }

    /// <inheritdoc />
    public async Task<OrderActionResult> MockMarkPaidAsync(
        int userId, int orderId, string password, CancellationToken ct = default)
    {
        // BANNERSH-182: gated by the same Not-Found error on every failure mode
        // (disabled, wrong password, foreign order, missing) so an attacker
        // can't probe the existence of the override from the response.
        if (!_testing.EnableMockPayment)
        {
            _logger.LogWarning(
                "MockMarkPaidAsync rejected: Testing:EnableMockPayment is false (user {UserId}, order {OrderId}).",
                userId, orderId);
            return OrderActionResult.Fail("Order not found.");
        }
        if (string.IsNullOrEmpty(password) ||
            !string.Equals(password, _testing.MockPaymentPassword, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "MockMarkPaidAsync rejected: wrong password (user {UserId}, order {OrderId}).",
                userId, orderId);
            return OrderActionResult.Fail("Invalid mock-payment password.");
        }

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || order.UserId != userId)
            return OrderActionResult.Fail("Order not found.");

        // Idempotent: if the order is already paid (or beyond), return success
        // so a retry from the frontend just succeeds rather than 422-ing.
        if (order.Status is OrderStatus.Paid or OrderStatus.InProduction or OrderStatus.ReadyToShip
                          or OrderStatus.Shipped or OrderStatus.Delivered)
        {
            var dto = await GetMineAsync(userId, orderId, ct);
            return dto is null
                ? OrderActionResult.Fail("Order not found.")
                : OrderActionResult.Ok(dto);
        }
        if (order.Status is not (OrderStatus.Draft or OrderStatus.PendingPayment))
            return OrderActionResult.Fail($"Order in status {order.Status} cannot be marked paid.");

        // Route through the same internal path used by the Stripe webhook so
        // the post-payment side effects (production-row seeding, confirmation
        // email, AI credit grant) all run consistently.
        await MarkPaidAsync(order.StripePaymentIntentId ?? string.Empty, order.Id, ct);

        _logger.LogInformation(
            "Order {OrderId} marked Paid via test-mode override (user {UserId}).",
            orderId, userId);

        var full = await GetMineAsync(userId, orderId, ct);
        return full is null
            ? OrderActionResult.Fail("Order not found.")
            : OrderActionResult.Ok(full);
    }

    /// <inheritdoc />
    public async Task<OrderActionResult> DeleteMineAsync(int userId, int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || order.UserId != userId || order.Deleted)
            return OrderActionResult.Fail("Order not found.");

        // BANNERSH-185: customers can only soft-delete orders that never made it past
        // payment. Paid / in-production / shipped / delivered orders are accounting
        // records and must remain visible.
        if (order.Status is not (OrderStatus.Draft or OrderStatus.PendingPayment or OrderStatus.Cancelled))
            return OrderActionResult.Fail($"Order in status {order.Status} cannot be deleted.");

        order.Deleted = true;
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Cancel any open PaymentIntent so the customer is not left with a
        // half-confirmed authorisation on Stripe's side.
        if (!string.IsNullOrEmpty(order.StripePaymentIntentId) &&
            order.Status is OrderStatus.Draft or OrderStatus.PendingPayment)
        {
            await _stripe.CancelPaymentIntentAsync(order.StripePaymentIntentId, ct);
        }

        _logger.LogInformation(
            "Order {OrderId} soft-deleted by user {UserId} (Status={Status}).",
            orderId, userId, order.Status);

        return OrderActionResult.Ok(new OrderDetailDto { Id = orderId });
    }

    /// <inheritdoc />
    public async Task<RetryPaymentResult> RetryPaymentAsync(int userId, int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || order.UserId != userId || order.Deleted)
            return RetryPaymentResult.Fail("Order not found.");

        // Only orders that are awaiting customer payment can be retried. Already-paid
        // (or beyond) orders return a benign success-with-null-secret so the frontend
        // can route the user to the confirmation page.
        if (order.Status is OrderStatus.Paid or OrderStatus.InProduction or OrderStatus.ReadyToShip
                          or OrderStatus.Shipped or OrderStatus.Delivered)
        {
            return new RetryPaymentResult(true, null, order.Id, ClientSecret: null,
                TotalNok: order.TotalNok, AlreadyPaid: true);
        }
        if (order.Status is not (OrderStatus.Draft or OrderStatus.PendingPayment))
            return RetryPaymentResult.Fail($"Order in status {order.Status} cannot be retried.");

        // Try to reuse the existing PaymentIntent first — its client_secret is still
        // valid for any retryable status (requires_payment_method etc.). If we get
        // nothing back (cancelled / succeeded / not-found / API error), mint a new PI.
        StripeIntentResult? intent = null;
        if (!string.IsNullOrEmpty(order.StripePaymentIntentId))
            intent = await _stripe.RetrievePaymentIntentAsync(order.StripePaymentIntentId, ct);
        if (intent is null)
        {
            intent = await _stripe.CreatePaymentIntentAsync(order.Id, userId, order.TotalNok, ct);
            order.StripePaymentIntentId = intent.PaymentIntentId;
        }

        // Make sure the status reflects "customer is paying" — moves Draft → PendingPayment
        // (orders persisted at PendingPayment already by CreateDraftAsync stay that way).
        if (order.Status == OrderStatus.Draft)
        {
            order.Status = OrderStatus.PendingPayment;
        }
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new RetryPaymentResult(true, null, order.Id,
            ClientSecret: intent.ClientSecret, TotalNok: order.TotalNok, AlreadyPaid: false);
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
        order.OrderState = OrderState.Paid;
        order.UpdatedAt = DateTime.UtcNow;

        // Seed initial Queued production rows for each item — only for orders that
        // actually go to production. CreditPack orders (BANNERSH-139) carry no banner
        // items and never enter production, so skip this entirely.
        if (order.OrderType != OrderType.CreditPack)
        {
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
        // whole MarkPaid flow on every send error. CreditPack orders don't get
        // the banner-shaped confirmation email (BANNERSH-139) — credits-granted
        // is its own success signal in the wizard UI.
        if (order.OrderType != OrderType.CreditPack)
        {
            var full = await OrderQueries.LoadFullOrderAsync(_db, order.Id, ct);
            if (full is not null)
                await TrySendOrderConfirmationAsync(full, ct);
        }
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
    // Customer-initiated design approval (BANNERSH-109)
    // ────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<OrderActionResult> ApproveDesignAsync(
        int orderId, int callerUserId, CancellationToken ct = default)
    {
        var order = await _db.Orders.FindAsync(new object?[] { orderId }, ct);
        if (order is null || order.UserId != callerUserId)
            return OrderActionResult.Fail("Order not found.");

        if (order.OrderState != OrderState.CustomerApproval)
        {
            return OrderActionResult.FailTransition(
                $"Order {orderId} is in state '{order.OrderState}'; " +
                $"approval requires CustomerApproval state.");
        }

        // Advance Order state.
        order.OrderState = OrderState.InProduction;
        order.Status = OrderStatus.InProduction;
        order.UpdatedAt = DateTime.UtcNow;

        // Mirror approval on any linked DesignRequest.
        var dr = await _db.DesignRequests
            .FirstOrDefaultAsync(r => r.OrderId == orderId, ct);
        if (dr is not null
            && (dr.Status == DesignRequestStatus.AwaitingApproval
                || dr.Status == DesignRequestStatus.Revised))
        {
            dr.Status = DesignRequestStatus.Approved;
            dr.CustomerApprovedAt = DateTime.UtcNow;
            dr.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "ApproveDesignAsync: order {OrderId} → InProduction (callerUserId={UserId}).",
            orderId, callerUserId);

        var full = await OrderQueries.LoadFullOrderAsync(_db, orderId, ct);
        // Reload dr (after SaveChangesAsync the tracked entity reflects the approved state)
        var drApproved = await OrderQueries.LoadDesignRequestForOrderAsync(_db, orderId, ct);
        return OrderActionResult.Ok(OrderMapper.ToDetailDto(full!, drApproved, _storage));
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

    private static CreateOrderDraftResult Fail(string error) => new(
        Success: false,
        Error: error,
        OrderId: 0,
        ClientSecret: string.Empty,
        TotalNok: 0m,
        Breakdown: new OrderPriceBreakdownDto());

    // ────────────────────────────────────────────────────────────────────────
    // Transactional email — fire-and-forget wrappers.
    // HTML body builders live in OrderEmailTemplates.cs.
    // Email failures must NEVER propagate to the caller (Stripe webhook); they
    // are logged and swallowed.
    // ────────────────────────────────────────────────────────────────────────

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
            var body = OrderEmailTemplates.BuildOrderConfirmationHtml(order);
            await _email.SendAsync(to, subject, body, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send order-confirmation email for order {OrderId} to {To}", order.Id, to);
        }
    }
}
