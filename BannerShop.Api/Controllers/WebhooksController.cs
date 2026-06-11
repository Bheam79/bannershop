using System.IO;
using BannerShop.Api.Services.AiCredits;
using BannerShop.Api.Services.DesignRequests;
using BannerShop.Api.Services.Orders;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Core.Enums;
using Microsoft.AspNetCore.Mvc;

namespace BannerShop.Api.Controllers;

/// <summary>
/// Stripe webhook receiver. Register  <c>POST /api/webhooks/stripe</c>  in the
/// Stripe dashboard (Dashboard → Developers → Webhooks → Add endpoint).
///
/// In production the backend runs on port 17080, so the full URL is:
///   <c>https://&lt;your-domain&gt;/api/webhooks/stripe</c>
///
/// The signing secret (<c>whsec_…</c>) must be saved in
/// <c>system_settings.stripe_webhook_secret</c> via /admin/settings.
///
/// For local development use the Stripe CLI:
///   <c>stripe listen --forward-to localhost:5000/api/webhooks/stripe</c>
///
/// Events handled:
///   <c>payment_intent.succeeded</c>  — banner orders, AI credit packs, manual design requests
///   <c>payment_intent.payment_failed</c> — marks the linked order as payment-failed
/// </summary>
[ApiController]
[Route("api/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IStripePaymentService _stripe;
    private readonly IOrderService _orders;
    private readonly IDesignRequestService _designRequests;
    private readonly IAiCreditService _aiCredits;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        IStripePaymentService stripe,
        IOrderService orders,
        IDesignRequestService designRequests,
        IAiCreditService aiCredits,
        ILogger<WebhooksController> logger)
    {
        _stripe = stripe;
        _orders = orders;
        _designRequests = designRequests;
        _aiCredits = aiCredits;
        _logger = logger;
    }

    // ── POST /api/webhooks/stripe ────────────────────────────────────────────
    [HttpPost("stripe")]
    public async Task<IActionResult> Stripe(CancellationToken ct)
    {
        // Stripe requires the raw body to verify the signature
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var signature = Request.Headers["Stripe-Signature"].ToString();

        // BANNERSH-213: unconditional Information-level entry log so admins can
        // verify in journalctl ("make logs") whether Stripe is actually hitting
        // the endpoint at all. The signature is intentionally NOT logged in full
        // — only its length and presence — to avoid leaking it.
        _logger.LogInformation(
            "Stripe webhook received: bodyBytes={Len}, hasSignature={HasSig}, sourceIp={Ip}",
            body.Length,
            !string.IsNullOrEmpty(signature),
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "(unknown)");

        if (string.IsNullOrEmpty(signature))
        {
            _logger.LogWarning("Stripe webhook rejected: missing Stripe-Signature header.");
            return BadRequest(new { error = "Missing Stripe-Signature header." });
        }

        var evt = await _stripe.VerifyAndParseEventAsync(body, signature, ct);
        if (evt is null)
        {
            // VerifyAndParseEventAsync already logs the reason (missing secret /
            // signature mismatch). Log a higher-visibility line here too so the
            // rejection is visible at Information level.
            _logger.LogWarning(
                "Stripe webhook rejected: signature/secret verification failed. " +
                "Check that 'stripe_webhook_secret' in /admin/settings matches the " +
                "signing secret shown in the Stripe Dashboard webhook page.");
            return BadRequest(new { error = "Invalid Stripe signature or payload." });
        }

        _logger.LogInformation(
            "Stripe webhook verified: type={Type}, pi={Pi}, metaType={MetaType}",
            evt.EventType, evt.PaymentIntentId, evt.MetadataType ?? "(none)");

        try
        {
            switch (evt.EventType)
            {
                case "payment_intent.succeeded":
                    await HandlePaymentIntentSucceededAsync(evt, ct);
                    break;

                case "payment_intent.payment_failed":
                    await _orders.MarkPaymentFailedAsync(evt.PaymentIntentId, evt.OrderIdFromMetadata, evt.FailureMessage, ct);
                    break;

                default:
                    _logger.LogInformation("Ignored Stripe event type {Type}", evt.EventType);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed handling Stripe webhook {Type} for PI {Pi}", evt.EventType, evt.PaymentIntentId);
            // Returning 500 makes Stripe retry; we want that on transient DB errors.
            return StatusCode(500, new { error = "Webhook handler error." });
        }

        return Ok(new { received = true });
    }

    // ── payment_intent.succeeded routing ────────────────────────────────────

    private async Task HandlePaymentIntentSucceededAsync(StripeWebhookEvent evt, CancellationToken ct)
    {
        switch (evt.MetadataType)
        {
            case "ai_credit_pack":
                // Credit pack purchase — grant credits idempotently (BANNERSH-69) AND
                // flip the synthetic CreditPack order to Paid (BANNERSH-139) so it shows
                // up correctly in transaction reports.
                if (evt.MetadataUserId is int uid && evt.MetadataCreditCount is int count)
                {
                    await _aiCredits.GrantAsync(
                        userId: uid,
                        count: count,
                        reason: CreditReason.CreditPack,
                        referenceId: evt.PaymentIntentId,
                        ct: ct);
                    _logger.LogInformation(
                        "Granted {Count} AI credits to user {UserId} via credit pack (PI {Pi}).",
                        count, uid, evt.PaymentIntentId);
                }
                else
                {
                    _logger.LogWarning(
                        "ai_credit_pack PI {Pi} missing userId or creditCount in metadata — cannot grant credits.",
                        evt.PaymentIntentId);
                }

                // Mark the linked Order paid (BANNERSH-139). Idempotent: MarkPaidAsync
                // short-circuits when the order is already in a >= Paid status. Safe to
                // call even for old credit-pack PIs that pre-date the Order row — the
                // resolver simply logs an unknown-PI warning and returns.
                await _orders.MarkPaidAsync(evt.PaymentIntentId, evt.OrderIdFromMetadata, ct);
                break;

            case "ai_design_standalone":
                // Dead-code guard: standalone AI design PIs were retired in BANNERSH-67.
                // Log and ignore — do NOT attempt to re-process.
                _logger.LogWarning(
                    "Received legacy ai_design_standalone PI {Pi} — this flow was retired in BANNERSH-67, ignoring.",
                    evt.PaymentIntentId);
                break;

            default:
                // Covers "banner_order" and any PI without a type field (pre-BANNERSH-69 orders).
                // Route to the existing Order and DesignRequest handlers.
                await _orders.MarkPaidAsync(evt.PaymentIntentId, evt.OrderIdFromMetadata, ct);
                await _designRequests.MarkPaidAndEnqueueAsync(evt.PaymentIntentId, ct);
                break;
        }
    }
}
