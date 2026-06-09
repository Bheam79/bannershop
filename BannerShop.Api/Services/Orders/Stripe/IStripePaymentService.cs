namespace BannerShop.Api.Services.Orders.Stripe;

/// <summary>The outcome of creating (or refreshing) a Stripe PaymentIntent for an order.</summary>
public record StripeIntentResult(string PaymentIntentId, string ClientSecret);

/// <summary>
/// Thin abstraction over the Stripe SDK so it can be mocked in tests / dev without keys.
/// </summary>
public interface IStripePaymentService
{
    /// <summary>
    /// Create a PaymentIntent for the given order amount.
    /// Amount is in NOK (will be converted to øre internally).
    /// </summary>
    Task<StripeIntentResult> CreatePaymentIntentAsync(
        int orderId,
        int userId,
        decimal amountNok,
        CancellationToken ct = default);

    /// <summary>
    /// Create a PaymentIntent for an AI credit pack purchase.
    /// Metadata includes <c>type=ai_credit_pack</c>, <c>userId</c>, <c>creditCount</c>,
    /// <c>orderId</c> (BANNERSH-139 — links the PI to the synthetic Order row), and
    /// an <c>idempotencyKey</c> (caller-supplied GUID) for deduplication.
    /// </summary>
    Task<StripeIntentResult> CreateCreditPackPaymentIntentAsync(
        int userId,
        int creditCount,
        decimal amountNok,
        string idempotencyKey,
        int? orderId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Update an existing PaymentIntent's amount (used when an order draft is recalculated).
    /// </summary>
    Task<StripeIntentResult> UpdatePaymentIntentAmountAsync(
        string paymentIntentId,
        decimal amountNok,
        CancellationToken ct = default);

    /// <summary>
    /// Cancel a PaymentIntent (used when an order is cancelled before payment).
    /// </summary>
    Task CancelPaymentIntentAsync(string paymentIntentId, CancellationToken ct = default);

    /// <summary>
    /// Verifies a Stripe webhook signature and returns the parsed event.
    /// Returns null when the signature is invalid.
    /// </summary>
    Task<StripeWebhookEvent?> VerifyAndParseEventAsync(string requestBody, string signatureHeader, CancellationToken ct = default);
}

/// <summary>Provider-agnostic representation of an inbound Stripe webhook event.</summary>
public record StripeWebhookEvent(
    string EventType,
    string PaymentIntentId,
    int? OrderIdFromMetadata,
    string? FailureMessage,
    /// <summary>Value of metadata["type"] — e.g. "ai_credit_pack", "banner_order".</summary>
    string? MetadataType = null,
    /// <summary>Value of metadata["userId"] parsed as int (for credit pack events).</summary>
    int? MetadataUserId = null,
    /// <summary>Value of metadata["creditCount"] parsed as int (for credit pack events).</summary>
    int? MetadataCreditCount = null);
