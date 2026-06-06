using System.Globalization;
using Microsoft.Extensions.Options;
using Stripe;

namespace BannerShop.Api.Services.Orders.Stripe;

/// <summary>
/// Live Stripe.net-backed implementation of <see cref="IStripePaymentService"/>.
/// </summary>
public class StripePaymentService : IStripePaymentService
{
    private readonly StripeOptions _options;
    private readonly ILogger<StripePaymentService> _logger;

    public StripePaymentService(IOptions<StripeOptions> options, ILogger<StripePaymentService> logger)
    {
        _options = options.Value;
        _logger = logger;
        StripeConfiguration.ApiKey = _options.SecretKey;
    }

    public async Task<StripeIntentResult> CreatePaymentIntentAsync(
        int orderId, int userId, decimal amountNok, CancellationToken ct = default)
    {
        var service = new PaymentIntentService();
        var intent = await service.CreateAsync(new PaymentIntentCreateOptions
        {
            Amount = ToMinorUnits(amountNok),
            Currency = _options.Currency,
            Metadata = new Dictionary<string, string>
            {
                ["orderId"] = orderId.ToString(CultureInfo.InvariantCulture),
                ["userId"]  = userId.ToString(CultureInfo.InvariantCulture)
            },
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true }
        }, cancellationToken: ct);

        return new StripeIntentResult(intent.Id, intent.ClientSecret);
    }

    public async Task<StripeIntentResult> UpdatePaymentIntentAmountAsync(
        string paymentIntentId, decimal amountNok, CancellationToken ct = default)
    {
        var service = new PaymentIntentService();
        var intent = await service.UpdateAsync(paymentIntentId, new PaymentIntentUpdateOptions
        {
            Amount = ToMinorUnits(amountNok)
        }, cancellationToken: ct);

        return new StripeIntentResult(intent.Id, intent.ClientSecret);
    }

    public async Task CancelPaymentIntentAsync(string paymentIntentId, CancellationToken ct = default)
    {
        try
        {
            var service = new PaymentIntentService();
            await service.CancelAsync(paymentIntentId, cancellationToken: ct);
        }
        catch (StripeException ex)
        {
            // Already cancelled / succeeded / not found — log and swallow so order cancellation still proceeds.
            _logger.LogInformation(ex, "Stripe PI {Pi} could not be cancelled: {Msg}", paymentIntentId, ex.Message);
        }
    }

    public StripeWebhookEvent? VerifyAndParseEvent(string requestBody, string signatureHeader)
    {
        try
        {
            var evt = EventUtility.ConstructEvent(
                requestBody,
                signatureHeader,
                _options.WebhookSecret,
                throwOnApiVersionMismatch: false);

            var intent = evt.Data.Object as PaymentIntent;
            if (intent is null)
                return new StripeWebhookEvent(evt.Type, string.Empty, null, null);

            int? orderId = null;
            if (intent.Metadata is { } meta &&
                meta.TryGetValue("orderId", out var raw) &&
                int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                orderId = parsed;
            }

            var failure = intent.LastPaymentError?.Message;
            return new StripeWebhookEvent(evt.Type, intent.Id, orderId, failure);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature verification failed: {Msg}", ex.Message);
            return null;
        }
    }

    private static long ToMinorUnits(decimal amountNok)
        => (long)decimal.Round(amountNok * 100m, 0, MidpointRounding.AwayFromZero);
}
