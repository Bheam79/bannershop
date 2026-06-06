namespace BannerShop.Api.Services.Orders.Stripe;

/// <summary>
/// Stripe configuration bound from the "Stripe" appsettings section.
/// </summary>
public class StripeOptions
{
    public const string SectionName = "Stripe";

    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;

    /// <summary>ISO 4217 currency code for PaymentIntents. NOK uses the 'øre' minor unit (×100).</summary>
    public string Currency { get; set; } = "nok";
}
