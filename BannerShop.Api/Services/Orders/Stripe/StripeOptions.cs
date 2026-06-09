namespace BannerShop.Api.Services.Orders.Stripe;

/// <summary>
/// Stripe configuration bound from the "Stripe" appsettings section.
///
/// BANNERSH-161: SecretKey / WebhookSecret / PublishableKey are NO LONGER read
/// from appsettings — the admin enters them via the settings panel
/// (system_settings rows 'stripe_secret_key' / 'stripe_publishable_key' /
/// 'stripe_webhook_secret'). Only the non-secret Currency setting remains.
/// </summary>
public class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>ISO 4217 currency code for PaymentIntents. NOK uses the 'øre' minor unit (×100).</summary>
    public string Currency { get; set; } = "nok";
}
