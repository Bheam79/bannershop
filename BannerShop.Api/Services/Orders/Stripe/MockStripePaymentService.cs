namespace BannerShop.Api.Services.Orders.Stripe;

/// <summary>
/// Fallback used when Stripe credentials are not configured (dev / tests).
/// Returns deterministic fake intent ids so the rest of the flow remains exercisable.
/// </summary>
public class MockStripePaymentService : IStripePaymentService
{
    private readonly ILogger<MockStripePaymentService> _logger;

    public MockStripePaymentService(ILogger<MockStripePaymentService> logger)
    {
        _logger = logger;
        _logger.LogWarning(
            "MockStripePaymentService is in use — Stripe credentials are not configured. Payments will NOT be processed.");
    }

    public Task<StripeIntentResult> CreatePaymentIntentAsync(int orderId, int userId, decimal amountNok, CancellationToken ct = default)
        => Task.FromResult(new StripeIntentResult(
            PaymentIntentId: $"pi_mock_{orderId}",
            ClientSecret: $"pi_mock_{orderId}_secret_dev"));

    public Task<StripeIntentResult> CreateCreditPackPaymentIntentAsync(int userId, int creditCount, decimal amountNok, string idempotencyKey, int? orderId = null, CancellationToken ct = default)
        => Task.FromResult(new StripeIntentResult(
            PaymentIntentId: $"pi_mock_pack_{userId}_{idempotencyKey[..8]}",
            ClientSecret: $"pi_mock_pack_{userId}_{idempotencyKey[..8]}_secret_dev"));

    public Task<StripeIntentResult> UpdatePaymentIntentAmountAsync(string paymentIntentId, decimal amountNok, CancellationToken ct = default)
        => Task.FromResult(new StripeIntentResult(paymentIntentId, $"{paymentIntentId}_secret_dev"));

    public Task CancelPaymentIntentAsync(string paymentIntentId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<StripeWebhookEvent?> VerifyAndParseEventAsync(string requestBody, string signatureHeader, CancellationToken ct = default)
    {
        _logger.LogWarning("Webhook called against MockStripePaymentService — rejecting.");
        return Task.FromResult<StripeWebhookEvent?>(null);
    }
}
