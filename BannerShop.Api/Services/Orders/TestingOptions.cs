namespace BannerShop.Api.Services.Orders;

/// <summary>
/// BANNERSH-182: configuration for the "mock payment" / "mark as paid"
/// password override used on the checkout page when real Stripe is
/// unavailable (or the operator wants to test the post-payment flow end
/// to end without a real card charge).
///
/// Bound from the <c>Testing</c> appsettings section.
///
/// In production the operator should either disable the override
/// (<c>Testing:EnableMockPayment = false</c>) or set a strong password
/// (<c>Testing:MockPaymentPassword</c>).
/// </summary>
public class TestingOptions
{
    public const string SectionName = "Testing";

    /// <summary>
    /// Whether <c>POST /api/orders/{id}/mock-pay</c> is enabled. When
    /// <c>false</c> (recommended for live production) the endpoint
    /// returns 404 regardless of the supplied password.
    /// Default <c>true</c> so dev / staging deployments work without
    /// extra wiring.
    /// </summary>
    public bool EnableMockPayment { get; set; } = true;

    /// <summary>
    /// Password the customer must type into the "Marker som betalt
    /// (testmodus)" modal on <c>PaymentView.vue</c> for the bypass to
    /// take effect. Defaults to the same hardcoded value the frontend
    /// modal hint references (<c>test1234</c>) so existing dev flows
    /// keep working.
    /// </summary>
    public string MockPaymentPassword { get; set; } = "test1234";
}
