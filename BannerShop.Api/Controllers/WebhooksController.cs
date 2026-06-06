using System.IO;
using BannerShop.Api.Services.DesignRequests;
using BannerShop.Api.Services.Orders;
using BannerShop.Api.Services.Orders.Stripe;
using Microsoft.AspNetCore.Mvc;

namespace BannerShop.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IStripePaymentService _stripe;
    private readonly IOrderService _orders;
    private readonly IDesignRequestService _designRequests;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        IStripePaymentService stripe,
        IOrderService orders,
        IDesignRequestService designRequests,
        ILogger<WebhooksController> logger)
    {
        _stripe = stripe;
        _orders = orders;
        _designRequests = designRequests;
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
        if (string.IsNullOrEmpty(signature))
            return BadRequest(new { error = "Missing Stripe-Signature header." });

        var evt = _stripe.VerifyAndParseEvent(body, signature);
        if (evt is null)
            return BadRequest(new { error = "Invalid Stripe signature or payload." });

        try
        {
            switch (evt.EventType)
            {
                case "payment_intent.succeeded":
                    // PaymentIntent can be for either an Order or a DesignRequest — try both.
                    await _orders.MarkPaidAsync(evt.PaymentIntentId, evt.OrderIdFromMetadata, ct);
                    await _designRequests.MarkPaidAndEnqueueAsync(evt.PaymentIntentId, ct);
                    break;

                case "payment_intent.payment_failed":
                    await _orders.MarkPaymentFailedAsync(evt.PaymentIntentId, evt.OrderIdFromMetadata, evt.FailureMessage, ct);
                    break;

                default:
                    _logger.LogDebug("Ignored Stripe event type {Type}", evt.EventType);
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
}
