namespace BannerShop.Api.Services.Shipping;

/// <summary>
/// Configuration bound from the "Bring" section of appsettings.
/// Credentials are issued by Bring/Posten at https://www.mybring.com/
/// </summary>
public class BringOptions
{
    public const string SectionName = "Bring";

    /// <summary>Bring API user identifier (typically the registered Mybring e-mail).</summary>
    public string ApiUid { get; set; } = string.Empty;

    /// <summary>Bring API key.</summary>
    public string ApiKey { get; set; } = string.Empty;

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

    /// <summary>Request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}
