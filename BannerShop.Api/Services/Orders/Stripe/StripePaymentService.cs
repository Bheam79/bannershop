using System.Globalization;
using BannerShop.Api.Services.SystemSettings;
using Microsoft.Extensions.Options;
using Stripe;

namespace BannerShop.Api.Services.Orders.Stripe;

/// <summary>
/// Live Stripe.net-backed implementation of <see cref="IStripePaymentService"/>.
///
/// Keys are resolved at call time with DB-first precedence (same pattern as OpenAI):
///   1. system_settings row (admin panel).
///   2. Fallback to appsettings Stripe:SecretKey / Stripe:WebhookSecret.
/// This means the admin can enter / update the key via the settings panel without
/// redeploying or restarting the service, and restricted keys (rk_live_…/rk_test_…)
/// are accepted in addition to standard secret keys (sk_live_…/sk_test_…).
/// </summary>
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
            Metadata = new Dictionary<string, string>
            {
                ["type"]    = "banner_order",
                ["orderId"] = orderId.ToString(CultureInfo.InvariantCulture),
                ["userId"]  = userId.ToString(CultureInfo.InvariantCulture)
            },
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true }
        }, reqOpts, cancellationToken: ct);

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

    public async Task<StripeWebhookEvent?> VerifyAndParseEventAsync(
        string requestBody, string signatureHeader, CancellationToken ct = default)
    {
        var webhookSecret = await GetEffectiveWebhookSecretAsync(ct);
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            _logger.LogWarning(
                "Stripe webhook secret NOT CONFIGURED — rejecting inbound webhook. " +
                "Set 'stripe_webhook_secret' in the admin settings panel or 'Stripe:WebhookSecret' in appsettings.");
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
    /// Returns the effective Stripe secret key. DB system setting wins over appsettings.
    /// Restricted keys (rk_live_… / rk_test_…) are valid.
    /// Throws <see cref="InvalidOperationException"/> if no key is found, so payment
    /// endpoints return 500 rather than silently using an empty key.
    /// </summary>
    private async Task<string> GetEffectiveSecretKeyAsync(CancellationToken ct)
    {
        // 1. Database setting (admin panel) — checked first so the admin can update
        //    the key via the settings panel without restarting the service.
        var dbKey = await _settings.GetValueAsync("stripe_secret_key", ct);
        if (!string.IsNullOrWhiteSpace(dbKey) && !IsPlaceholderKey(dbKey))
        {
            _logger.LogDebug("Stripe key resolved from db:stripe_secret_key ({Mask})", MaskKey(dbKey));
            return dbKey;
        }

        // 2. Config-file value.
        var cfgKey = _options.SecretKey;
        if (!string.IsNullOrWhiteSpace(cfgKey) && !IsPlaceholderKey(cfgKey))
        {
            _logger.LogDebug("Stripe key resolved from appsettings:Stripe:SecretKey ({Mask})", MaskKey(cfgKey));
            return cfgKey;
        }

        _logger.LogError(
            "Stripe secret key NOT CONFIGURED. " +
            "DB setting 'stripe_secret_key' = {DbStatus}; appsettings 'Stripe:SecretKey' = {CfgStatus}. " +
            "Enter the key via the admin settings panel (supports sk_live_…, sk_test_…, rk_live_…, rk_test_…).",
            DescribeKey(dbKey), DescribeKey(cfgKey));

        throw new InvalidOperationException(
            "Stripe is not configured. Enter a secret key in the admin settings panel.");
    }

    /// <summary>
    /// Returns the effective Stripe webhook secret. DB setting wins over appsettings.
    /// Returns null if neither is set.
    /// </summary>
    private async Task<string?> GetEffectiveWebhookSecretAsync(CancellationToken ct)
    {
        var dbSecret = await _settings.GetValueAsync("stripe_webhook_secret", ct);
        if (!string.IsNullOrWhiteSpace(dbSecret) && !IsPlaceholderKey(dbSecret))
            return dbSecret;

        var cfgSecret = _options.WebhookSecret;
        if (!string.IsNullOrWhiteSpace(cfgSecret) && !IsPlaceholderKey(cfgSecret))
            return cfgSecret;

        return null;
    }

    private static bool IsPlaceholderKey(string key) =>
        key.StartsWith("sk_test_REPLACE_", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("whsec_REPLACE_", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase);

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
