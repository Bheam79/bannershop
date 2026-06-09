namespace BannerShop.Api.Services.Shipping;

/// <summary>
/// Configuration bound from the "Bring" section of appsettings.
/// Credentials are issued by Bring/Posten at https://www.mybring.com/.
///
/// BANNERSH-143: production credentials are hardcoded as defaults so the
/// shipping calculator works out-of-the-box without secrets management.
/// Override via appsettings if a different Mybring account is needed.
/// </summary>
public class BringOptions
{
    public const string SectionName = "Bring";

    /// <summary>Bring API user identifier (typically the registered Mybring e-mail).</summary>
    public string ApiUid { get; set; } = "post@beatgrid.no";

    /// <summary>Bring API key.</summary>
    public string ApiKey { get; set; } = "fce1db12-90bb-40b4-9ee4-81c5e40dae32";

    /// <summary>
    /// Mybring customer number (parties.customerNumber) used to apply the
    /// negotiated rate agreement on the Shipping Guide API.
    /// </summary>
    public string CustomerNumber { get; set; } = "20027039252";

    /// <summary>Sender postal code (the shop address).</summary>
    public string SenderPostalCode { get; set; } = "0001";

    /// <summary>Sender country code (ISO 3166-1 alpha-2).</summary>
    public string SenderCountryCode { get; set; } = "NO";

    /// <summary>Identifier sent in the X-Bring-Client-URL header (good citizenship for the API).</summary>
    public string ClientUrl { get; set; } = "https://bannershop.no";

    /// <summary>
    /// Comma-separated Bring product codes to request.
    /// Defaults to SERVICEPAKKE (Bedriftspakke, business parcel) — fits parcels up to 240 cm length.
    /// </summary>
    public string ProductCodes { get; set; } = "SERVICEPAKKE";

    /// <summary>Base URL of the Bring Shipping Guide v2 API.</summary>
    public string BaseUrl { get; set; } = "https://api.bring.com";

    /// <summary>
    /// Path to the Shipping Guide rates endpoint, appended to <see cref="BaseUrl"/>.
    /// </summary>
    public string RatesPath { get; set; } = "/shippingguide/v2";

    /// <summary>
    /// Bring booking API base URL (reserved for future booking integration; the
    /// rates calculator uses <see cref="BaseUrl"/> + <see cref="RatesPath"/>).
    /// </summary>
    public string BookingUrl { get; set; } = "https://api.bring.com/booking/api";

    /// <summary>
    /// When true, request Bring e-Varsling (e-notification of delivery) for any
    /// product in <see cref="EVarslingProducts"/>. Defaults to false — the shop
    /// handles its own delivery notifications via email/SMS.
    /// </summary>
    public bool EVarsling { get; set; } = false;

    /// <summary>
    /// Comma-separated product codes that support e-Varsling. Used only when
    /// <see cref="EVarsling"/> is true.
    /// </summary>
    public string EVarslingProducts { get; set; } = "SERVICEPAKKE,BPAKKE_DOR-DOR,PA_DOREN,EKSPRESS09";

    /// <summary>Request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}
