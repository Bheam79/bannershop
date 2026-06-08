using System.Security.Claims;
using BannerShop.Api.Services.AiCredits;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Controllers;

/// <summary>
/// AI generation credit pool endpoints (BANNERSH-65, BANNERSH-69).
/// </summary>
[ApiController]
[Route("api/ai-credits")]
public class AiCreditsController : ControllerBase
{
    private readonly IAiCreditService _credits;
    private readonly IStripePaymentService _stripe;
    private readonly BannerShopDbContext _db;

    public AiCreditsController(
        IAiCreditService credits,
        IStripePaymentService stripe,
        BannerShopDbContext db)
    {
        _credits = credits;
        _stripe = stripe;
        _db = db;
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

    // ── POST /api/ai-credits/packs/buy ───────────────────────────────────────
    /// <summary>
    /// Initiates a credit-pack purchase. Returns a Stripe PaymentIntent client secret
    /// the frontend uses to confirm payment. Credits are granted by the Stripe webhook
    /// after payment_intent.succeeded fires (BANNERSH-69).
    /// </summary>
    [HttpPost("packs/buy")]
    [Authorize]
    public async Task<IActionResult> BuyCreditPack(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        // Load credit-pack pricing parameters (seeded by BANNERSH-65).
        var pricingDict = await _db.PricingParameters
            .AsNoTracking()
            .Where(p => p.Key == "ai_credit_pack_price_nok" || p.Key == "ai_credit_pack_count")
            .ToDictionaryAsync(p => p.Key, p => p.Value, ct);

        var priceNok    = pricingDict.GetValueOrDefault("ai_credit_pack_price_nok", 29m);
        var creditCount = (int)pricingDict.GetValueOrDefault("ai_credit_pack_count", 10m);

        // A client-side idempotency key prevents duplicate PIs if the request is retried.
        var idempotencyKey = Guid.NewGuid().ToString("N");

        var intent = await _stripe.CreateCreditPackPaymentIntentAsync(
            userId, creditCount, priceNok, idempotencyKey, ct);

        return Ok(new
        {
            clientSecret = intent.ClientSecret,
            creditCount  = creditCount,
            priceNok     = priceNok
        });
    }

    private int GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : 0;
    }
}
