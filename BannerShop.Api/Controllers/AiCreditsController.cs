using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;
using BannerShop.Api.Services.AiCredits;
using BannerShop.Api.Services.Orders;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Controllers;

/// <summary>
/// AI generation credit pool endpoints (BANNERSH-65, BANNERSH-69, BANNERSH-137).
/// </summary>
[ApiController]
[Route("api/ai-credits")]
public class AiCreditsController : ControllerBase
{
    private readonly IAiCreditService _credits;
    private readonly IStripePaymentService _stripe;
    private readonly IOrderService _orders;
    private readonly BannerShopDbContext _db;
    private readonly ILogger<AiCreditsController> _log;

    public AiCreditsController(
        IAiCreditService credits,
        IStripePaymentService stripe,
        IOrderService orders,
        BannerShopDbContext db,
        ILogger<AiCreditsController> log)
    {
        _credits = credits;
        _stripe = stripe;
        _orders = orders;
        _db = db;
        _log = log;
    }

    // ── GET /api/ai-credits/me ───────────────────────────────────────────────
    /// <summary>Returns the current credit balance and free-generation status for the caller.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var balance = await _credits.GetBalanceWithDetailsAsync(userId, ct);
        return Ok(new
        {
            creditsRemaining = balance.CreditsRemaining,
            hasUsedFreeGeneration = balance.HasUsedFreeGeneration
        });
    }

    // ── GET /api/ai-credits/packs ────────────────────────────────────────────
    /// <summary>
    /// Public credit-pack info — price and credit count for both pack tiers (BANNERSH-137).
    /// Used by widgets that need to display the buy CTA without going through the paywall 402 flow.
    /// </summary>
    [HttpGet("packs")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCreditPackInfo(CancellationToken ct)
    {
        var pricingDict = await LoadAllPackPricingAsync(ct);

        var smallPrice = pricingDict.GetValueOrDefault("ai_credit_pack_price_nok", 29m);
        var smallCount = (int)pricingDict.GetValueOrDefault("ai_credit_pack_count", 5m);
        var largePrice = pricingDict.GetValueOrDefault("ai_credit_pack_large_price_nok", 95m);
        var largeCount = (int)pricingDict.GetValueOrDefault("ai_credit_pack_large_count", 20m);

        return Ok(new
        {
            small = new { priceNok = smallPrice, creditCount = smallCount },
            large = new { priceNok = largePrice, creditCount = largeCount }
        });
    }

    // ── POST /api/ai-credits/packs/buy ───────────────────────────────────────
    /// <summary>
    /// Initiates a credit-pack purchase for the chosen tier (BANNERSH-137).
    /// Body: { "pack": "small" | "large" } — defaults to "small" for backward compatibility.
    /// Returns a Stripe PaymentIntent client secret; credits are granted by the Stripe webhook.
    /// </summary>
    [HttpPost("packs/buy")]
    [Authorize]
    public async Task<IActionResult> BuyCreditPack(
        [FromBody] BuyCreditPackRequest? request,
        CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var pack = (request?.Pack ?? "small").ToLowerInvariant();
        if (pack != "small" && pack != "large")
            return BadRequest(new { error = "Invalid pack. Must be 'small' or 'large'." });

        var pricingDict = await LoadAllPackPricingAsync(ct);

        decimal priceNok;
        int creditCount;

        if (pack == "large")
        {
            priceNok    = pricingDict.GetValueOrDefault("ai_credit_pack_large_price_nok", 95m);
            creditCount = (int)pricingDict.GetValueOrDefault("ai_credit_pack_large_count", 20m);
        }
        else
        {
            priceNok    = pricingDict.GetValueOrDefault("ai_credit_pack_price_nok", 29m);
            creditCount = (int)pricingDict.GetValueOrDefault("ai_credit_pack_count", 5m);
        }

        // A client-side idempotency key prevents duplicate PIs if the request is retried.
        var idempotencyKey = Guid.NewGuid().ToString("N");

        // BANNERSH-139: create a synthetic Order row so credit-pack revenue shows up in
        // transaction reports. The order is hidden from the admin "Ordrer" list by default
        // (filtered out by OrderType=CreditPack unless explicitly requested).
        var order = new Order
        {
            UserId       = userId,
            OrderType    = OrderType.CreditPack,
            OrderState   = OrderState.Draft,
            Status       = OrderStatus.Draft,
            DeliveryType = DeliveryType.Pickup, // credit packs have no physical delivery
            TotalNok     = priceNok,
            CreatedAt    = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow,
            Items        = new List<OrderItem>
            {
                new OrderItem
                {
                    BannerSizeId   = null,
                    HeightCm       = 0,
                    Quantity       = 1,
                    AreaSqm        = 0m,
                    UnitPriceNok   = priceNok,
                    EyeletOption   = EyeletOption.None,
                    EyeletCount    = 0,
                    EyeletFeeNok   = 0m,
                    LineTotalNok   = priceNok,
                    Notes          = $"AI generation pack — {creditCount} credits"
                }
            }
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        var intent = await _stripe.CreateCreditPackPaymentIntentAsync(
            userId, creditCount, priceNok, idempotencyKey, order.Id, ct);

        // Persist the PI id on the Order so the webhook can resolve it back.
        order.StripePaymentIntentId = intent.PaymentIntentId;
        order.Status = OrderStatus.PendingPayment;
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            clientSecret = intent.ClientSecret,
            creditCount  = creditCount,
            priceNok     = priceNok,
            pack         = pack,
            orderId      = order.Id
        });
    }

    // ── POST /api/ai-credits/packs/activate ─────────────────────────────────
    /// <summary>
    /// Called by the frontend immediately after <c>confirmCardPayment</c> succeeds
    /// to grant credits synchronously, without waiting for the Stripe webhook
    /// (BANNERSH-213). The webhook may also fire later; <see cref="IAiCreditService.GrantAsync"/>
    /// is idempotent via <c>referenceId = paymentIntentId</c> so double-grants are prevented.
    ///
    /// Verifies that:
    ///  1. A CreditPack order exists for this PI that belongs to the current user.
    ///  2. Stripe confirms the PI has succeeded (mock always returns true in tests).
    ///
    /// Returns the updated credit balance so the badge can be refreshed in one round-trip.
    /// </summary>
    [HttpPost("packs/activate")]
    [Authorize]
    public async Task<IActionResult> ActivateCreditPack(
        [FromBody] ActivateCreditPackRequest? request,
        CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request?.PaymentIntentId))
            return BadRequest(new { error = "paymentIntentId is required." });

        var piId = request.PaymentIntentId;

        // 1. Find the linked CreditPack order owned by this user.
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o =>
                o.StripePaymentIntentId == piId &&
                o.UserId == userId &&
                o.OrderType == OrderType.CreditPack,
                ct);

        if (order is null)
        {
            _log.LogWarning("ActivateCreditPack: no CreditPack order found for PI {Pi} / user {Uid}.", piId, userId);
            return NotFound(new { error = "Credit pack order not found." });
        }

        // 2. If the webhook already processed this PI the order is already Paid — nothing to do.
        if (order.Status != OrderStatus.Paid)
        {
            // Verify with Stripe that the PI has actually succeeded.
            var succeeded = await _stripe.IsPaymentIntentSucceededAsync(piId, ct);
            if (!succeeded)
            {
                _log.LogWarning("ActivateCreditPack: PI {Pi} not yet succeeded for user {Uid}.", piId, userId);
                return BadRequest(new { error = "Payment not yet confirmed. Please try again in a moment." });
            }

            // 3. Parse credit count from the order item's Notes.
            var creditCount = ParseCreditCountFromNotes(order.Items.FirstOrDefault()?.Notes);
            if (creditCount <= 0)
            {
                _log.LogError("ActivateCreditPack: could not parse credit count for order {OrderId}.", order.Id);
                return StatusCode(500, new { error = "Could not determine credit count from order." });
            }

            // 4. Grant credits idempotently (PI id is the referenceId guard).
            await _credits.GrantAsync(userId, creditCount, CreditReason.CreditPack, piId, ct);

            // 5. Flip the order to Paid (mirrors what the webhook does; MarkPaidAsync is idempotent).
            await _orders.MarkPaidAsync(piId, order.Id, ct);

            _log.LogInformation(
                "ActivateCreditPack: granted {Count} credits to user {Uid} via PI {Pi} (order {OrderId}).",
                creditCount, userId, piId, order.Id);
        }

        var balance = await _credits.GetBalanceAsync(userId, ct);
        return Ok(new { creditsRemaining = balance });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses the credit count from an OrderItem.Notes string of the form
    /// <c>"AI generation pack — {count} credits"</c>.
    /// Returns 0 when no match is found.
    /// </summary>
    private static int ParseCreditCountFromNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return 0;
        var m = Regex.Match(notes, @"(\d+)\s+credits", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    /// <summary>
    /// Loads all credit-pack pricing parameters at once to avoid multiple round-trips.
    /// Falls back to the documented defaults when rows are missing.
    /// </summary>
    private async Task<Dictionary<string, decimal>> LoadAllPackPricingAsync(CancellationToken ct)
    {
        return await _db.PricingParameters
            .AsNoTracking()
            .Where(p => p.Key == "ai_credit_pack_price_nok"
                     || p.Key == "ai_credit_pack_count"
                     || p.Key == "ai_credit_pack_large_price_nok"
                     || p.Key == "ai_credit_pack_large_count")
            .ToDictionaryAsync(p => p.Key, p => p.Value, ct);
    }

    private int GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : 0;
    }
}

/// <summary>Body for POST /api/ai-credits/packs/buy (BANNERSH-137).</summary>
public class BuyCreditPackRequest
{
    /// <summary>"small" or "large". Defaults to "small" when omitted.</summary>
    public string Pack { get; set; } = "small";
}

/// <summary>Body for POST /api/ai-credits/packs/activate (BANNERSH-213).</summary>
public class ActivateCreditPackRequest
{
    /// <summary>
    /// The Stripe PaymentIntent id (e.g. "pi_3Abc…") returned inside the
    /// <c>clientSecret</c> by <c>POST /api/ai-credits/packs/buy</c>.
    /// The frontend extracts it as <c>clientSecret.split('_secret_')[0]</c>.
    /// </summary>
    public string? PaymentIntentId { get; set; }
}
