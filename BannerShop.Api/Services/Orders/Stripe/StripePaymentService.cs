using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BannerShop.Api.Services.SystemSettings;
using Microsoft.Extensions.Options;
using Stripe;

namespace BannerShop.Api.Services.Orders.Stripe;

/// <summary>
/// Live Stripe.net-backed implementation of <see cref="IStripePaymentService"/>.
///
/// BANNERSH-161: keys are read EXCLUSIVELY from the database
/// (system_settings rows 'stripe_secret_key' / 'stripe_webhook_secret') — there
/// is no appsettings fallback any more. The admin enters / updates them via the
/// settings panel; restricted keys (rk_live_…/rk_test_…) are accepted in
/// addition to standard secret keys (sk_live_…/sk_test_…). When no key is set,
/// payment endpoints throw rather than silently failing.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Live Stripe.NET wrapper — tested via integration against the Stripe test API")]
public class StripePaymentService : IStripePaymentService
{
    private readonly StripeOptions _options;
    private readonly ISystemSettingsService _settings;
    private readonly ILogger<StripePaymentService> _logger;

    public StripePaymentService(
        IOptions<StripeOptions> options,
        ISystemSettingsService settings,
        ILogger<StripePaymentService> logger)
    {
        _options = options.Value;
        _settings = settings;
        _logger = logger;
    }

    public async Task<StripeIntentResult> CreatePaymentIntentAsync(
        int orderId, int userId, decimal amountNok, CancellationToken ct = default)
    {
        var apiKey = await GetEffectiveSecretKeyAsync(ct);
        var reqOpts = new RequestOptions { ApiKey = apiKey };

        var service = new PaymentIntentService();
        var intent = await service.CreateAsync(new PaymentIntentCreateOptions
        {
            Amount = ToMinorUnits(amountNok),
            Currency = _options.Currency,
            // Banner orders use manual capture: funds are authorized at checkout
            // and captured when the item is shipped (see CapturePaymentIntentAsync).
            CaptureMethod = "manual",
            Metadata = new Dictionary<string, string>
            {
                ["type"]    = "banner_order",
                ["orderId"] = orderId.ToString(CultureInfo.InvariantCulture),
                ["userId"]  = userId.ToString(CultureInfo.InvariantCulture)
            },
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true }
        }, reqOpts, cancellationToken: ct);

        _logger.LogInformation(
            "Created Stripe PI {Pi} in {Mode} mode for banner order (orderId={OrderId}, userId={UserId}, amountNok={Amount}). " +
            "Webhook deliveries for this PI appear under the matching test/live tab in the Stripe Dashboard.",
            intent.Id, GetKeyMode(apiKey), orderId, userId, amountNok);

        return new StripeIntentResult(intent.Id, intent.ClientSecret);
    }

    public async Task<StripeIntentResult> CreateCreditPackPaymentIntentAsync(
        int userId, int creditCount, decimal amountNok, string idempotencyKey,
        int? orderId = null, CancellationToken ct = default)
    {
        var apiKey = await GetEffectiveSecretKeyAsync(ct);
        var reqOpts = new RequestOptions { ApiKey = apiKey };

        var metadata = new Dictionary<string, string>
        {
            ["type"]            = "ai_credit_pack",
            ["userId"]          = userId.ToString(CultureInfo.InvariantCulture),
            ["creditCount"]     = creditCount.ToString(CultureInfo.InvariantCulture),
            ["idempotencyKey"]  = idempotencyKey
        };
        if (orderId is int oid)
            metadata["orderId"] = oid.ToString(CultureInfo.InvariantCulture);

        var service = new PaymentIntentService();
        var intent = await service.CreateAsync(new PaymentIntentCreateOptions
        {
            Amount = ToMinorUnits(amountNok),
            Currency = _options.Currency,
            Metadata = metadata,
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true }
        }, reqOpts, cancellationToken: ct);

        _logger.LogInformation(
            "Created Stripe PI {Pi} in {Mode} mode for AI credit pack ({Credits} credits, {Amount} NOK, userId={UserId}, orderId={OrderId}). " +
            "Webhook deliveries for this PI appear under the matching test/live tab in the Stripe Dashboard.",
            intent.Id, GetKeyMode(apiKey), creditCount, amountNok, userId, orderId);

        return new StripeIntentResult(intent.Id, intent.ClientSecret);
    }

    public async Task<StripeIntentResult> UpdatePaymentIntentAmountAsync(
        string paymentIntentId, decimal amountNok, CancellationToken ct = default)
    {
        var apiKey = await GetEffectiveSecretKeyAsync(ct);
        var reqOpts = new RequestOptions { ApiKey = apiKey };

        var service = new PaymentIntentService();
        var intent = await service.UpdateAsync(paymentIntentId, new PaymentIntentUpdateOptions
        {
            Amount = ToMinorUnits(amountNok)
        }, reqOpts, cancellationToken: ct);

        return new StripeIntentResult(intent.Id, intent.ClientSecret);
    }

    public async Task CancelPaymentIntentAsync(string paymentIntentId, CancellationToken ct = default)
    {
        try
        {
            var apiKey = await GetEffectiveSecretKeyAsync(ct);
            var reqOpts = new RequestOptions { ApiKey = apiKey };

            var service = new PaymentIntentService();
            await service.CancelAsync(paymentIntentId, null, reqOpts, cancellationToken: ct);
        }
        catch (StripeException ex)
        {
            // Already cancelled / succeeded / not found — log and swallow so order cancellation still proceeds.
            _logger.LogInformation(ex, "Stripe PI {Pi} could not be cancelled: {Msg}", paymentIntentId, ex.Message);
        }
    }

    public async Task CapturePaymentIntentAsync(string paymentIntentId, CancellationToken ct = default)
    {
        try
        {
            var apiKey = await GetEffectiveSecretKeyAsync(ct);
            var reqOpts = new RequestOptions { ApiKey = apiKey };

            var service = new PaymentIntentService();
            await service.CaptureAsync(paymentIntentId, options: null, reqOpts, cancellationToken: ct);

            _logger.LogInformation("Captured Stripe PI {Pi}.", paymentIntentId);
        }
        catch (StripeException ex)
        {
            // Already captured or not in a capturable state — log and let the caller decide.
            _logger.LogWarning(ex, "Stripe PI {Pi} could not be captured: {Msg}", paymentIntentId, ex.Message);
            throw;
        }
    }

    public async Task<StripeIntentResult?> RetrievePaymentIntentAsync(
        string paymentIntentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId)) return null;
        try
        {
            var apiKey = await GetEffectiveSecretKeyAsync(ct);
            var reqOpts = new RequestOptions { ApiKey = apiKey };

            var service = new PaymentIntentService();
            var intent = await service.GetAsync(paymentIntentId, requestOptions: reqOpts, cancellationToken: ct);

            // Stripe statuses where the existing client_secret can still drive a
            // successful confirmation: requires_payment_method (after a previous
            // failure), requires_confirmation, requires_action. Other statuses
            // (canceled, succeeded, processing) mean a retry would either be
            // impossible or already in-flight — caller should mint a new PI.
            var status = intent.Status;
            if (status is "requires_payment_method" or "requires_confirmation" or "requires_action")
                return new StripeIntentResult(intent.Id, intent.ClientSecret);

            _logger.LogInformation(
                "Stripe PI {Pi} retrieved but status '{Status}' is not retryable.",
                paymentIntentId, status);
            return null;
        }
        catch (StripeException ex)
        {
            _logger.LogInformation(ex, "Stripe PI {Pi} could not be retrieved: {Msg}",
                paymentIntentId, ex.Message);
            return null;
        }
    }

    public async Task<bool> IsPaymentIntentSucceededAsync(
        string paymentIntentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId)) return false;
        try
        {
            var apiKey = await GetEffectiveSecretKeyAsync(ct);
            var reqOpts = new RequestOptions { ApiKey = apiKey };

            var service = new PaymentIntentService();
            var intent = await service.GetAsync(paymentIntentId, requestOptions: reqOpts, cancellationToken: ct);

            return intent.Status == "succeeded";
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "IsPaymentIntentSucceededAsync: PI {Pi} lookup failed: {Msg}",
                paymentIntentId, ex.Message);
            return false;
        }
    }

    public async Task<StripeWebhookEvent?> VerifyAndParseEventAsync(
        string requestBody, string signatureHeader, CancellationToken ct = default)
    {
        var webhookSecret = await GetEffectiveWebhookSecretAsync(ct);
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            _logger.LogWarning(
                "Stripe webhook secret NOT CONFIGURED — rejecting inbound webhook. " +
                "Set 'stripe_webhook_secret' in the admin settings panel.");
            return null;
        }

        try
        {
            var evt = EventUtility.ConstructEvent(
                requestBody,
                signatureHeader,
                webhookSecret,
                throwOnApiVersionMismatch: false);

            var intent = evt.Data.Object as PaymentIntent;
            if (intent is null)
                return new StripeWebhookEvent(evt.Type, string.Empty, null, null);

            var meta = intent.Metadata;

            int? orderId = null;
            if (meta is not null &&
                meta.TryGetValue("orderId", out var rawOrderId) &&
                int.TryParse(rawOrderId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedOrderId))
            {
                orderId = parsedOrderId;
            }

            string? metaType = meta is not null && meta.TryGetValue("type", out var t) ? t : null;

            int? metaUserId = null;
            if (meta is not null &&
                meta.TryGetValue("userId", out var rawUserId) &&
                int.TryParse(rawUserId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedUserId))
            {
                metaUserId = parsedUserId;
            }

            int? metaCreditCount = null;
            if (meta is not null &&
                meta.TryGetValue("creditCount", out var rawCreditCount) &&
                int.TryParse(rawCreditCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCreditCount))
            {
                metaCreditCount = parsedCreditCount;
            }

            var failure = intent.LastPaymentError?.Message;
            return new StripeWebhookEvent(
                evt.Type, intent.Id, orderId, failure,
                MetadataType: metaType,
                MetadataUserId: metaUserId,
                MetadataCreditCount: metaCreditCount);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe webhook signature verification failed: {Msg}", ex.Message);
            return null;
        }
    }

    // ── Key resolution ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the effective Stripe secret key from the database.
    /// Restricted keys (rk_live_… / rk_test_…) are valid.
    /// Throws <see cref="InvalidOperationException"/> if the key is not configured,
    /// so payment endpoints return 500 rather than silently using an empty key.
    ///
    /// BANNERSH-161: appsettings fallback removed — the admin enters the key via
    /// the settings panel ('stripe_secret_key' row).
    /// </summary>
    private async Task<string> GetEffectiveSecretKeyAsync(CancellationToken ct)
    {
        var dbKey = await _settings.GetValueAsync("stripe_secret_key", ct);
        if (!string.IsNullOrWhiteSpace(dbKey) && !IsPlaceholderKey(dbKey))
        {
            _logger.LogDebug("Stripe key resolved from db:stripe_secret_key ({Mask})", MaskKey(dbKey));
            return dbKey;
        }

        _logger.LogError(
            "Stripe secret key NOT CONFIGURED. DB setting 'stripe_secret_key' = {DbStatus}. " +
            "Enter the key via the admin settings panel (supports sk_live_…, sk_test_…, rk_live_…, rk_test_…).",
            DescribeKey(dbKey));

        throw new InvalidOperationException(
            "Stripe is not configured. Enter a secret key in the admin settings panel.");
    }

    /// <summary>
    /// Returns the effective Stripe webhook secret from the database.
    /// Returns null if not configured.
    ///
    /// BANNERSH-161: appsettings fallback removed.
    /// </summary>
    private async Task<string?> GetEffectiveWebhookSecretAsync(CancellationToken ct)
    {
        var dbSecret = await _settings.GetValueAsync("stripe_webhook_secret", ct);
        if (!string.IsNullOrWhiteSpace(dbSecret) && !IsPlaceholderKey(dbSecret))
            return dbSecret;

        return null;
    }

    private static bool IsPlaceholderKey(string key) =>
        key.StartsWith("sk_test_REPLACE_", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("whsec_REPLACE_", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns "test" / "live" / "unknown" from a Stripe secret-key prefix.
    /// Used in PI-creation logs so admins can verify their app's mode matches
    /// the Stripe Dashboard tab they're checking webhook deliveries on
    /// (test PIs only show in test webhooks; live PIs only show in live webhooks).
    /// </summary>
    private static string GetKeyMode(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey)) return "unknown";
        if (apiKey.StartsWith("sk_test_", StringComparison.OrdinalIgnoreCase) ||
            apiKey.StartsWith("rk_test_", StringComparison.OrdinalIgnoreCase))
            return "test";
        if (apiKey.StartsWith("sk_live_", StringComparison.OrdinalIgnoreCase) ||
            apiKey.StartsWith("rk_live_", StringComparison.OrdinalIgnoreCase))
            return "live";
        return "unknown";
    }

    private static string DescribeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "unset";
        if (IsPlaceholderKey(key)) return $"placeholder({MaskKey(key)})";
        return $"set({MaskKey(key)}, {key.Length} chars)";
    }

    private static string MaskKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "(empty)";
        if (key.Length <= 10) return new string('*', key.Length);
        return key[..6] + "…" + key[^4..];
    }

    private static long ToMinorUnits(decimal amountNok)
        => (long)decimal.Round(amountNok * 100m, 0, MidpointRounding.AwayFromZero);
}
