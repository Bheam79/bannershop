using System.Text.Json.Serialization;

namespace BannerShop.Api.Services.Shipping;

// ── Request DTOs (Bring Shipping Guide 2.0) ──────────────────────────────────
// Reference: https://developer.bring.com/api/shipping-guide_2/

internal class BringShipmentRequest
{
    [JsonPropertyName("consignments")]
    public List<BringConsignment> Consignments { get; set; } = new();
}

internal class BringConsignment
{
    [JsonPropertyName("fromCountryCode")]
    public string FromCountryCode { get; set; } = "NO";

    [JsonPropertyName("fromPostalCode")]
    public string FromPostalCode { get; set; } = string.Empty;

    [JsonPropertyName("toCountryCode")]
    public string ToCountryCode { get; set; } = "NO";

    [JsonPropertyName("toPostalCode")]
    public string ToPostalCode { get; set; } = string.Empty;

    /// <summary>
    /// Mybring sender/recipient agreement info. Omitted when no customer number
    /// is configured (BANNERSH-143).
    /// </summary>
    [JsonPropertyName("parties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BringConsignmentParties? Parties { get; set; }

    [JsonPropertyName("products")]
    public List<BringProductRef> Products { get; set; } = new();

    [JsonPropertyName("packages")]
    public List<BringPackage> Packages { get; set; } = new();
}

internal class BringConsignmentParties
{
    [JsonPropertyName("sender")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BringConsignmentParty? Sender { get; set; }
}

internal class BringConsignmentParty
{
    [JsonPropertyName("customerNumber")]
    public string CustomerNumber { get; set; } = string.Empty;
}

internal class BringProductRef
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Per-product additional services (e.g. EVARSLING). Omitted when none.
    /// </summary>
    [JsonPropertyName("additionalServices")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BringAdditionalService>? AdditionalServices { get; set; }
}

internal class BringAdditionalService
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

internal class BringPackage
{
    /// <summary>
    /// Package identifier — required by Bring API v2. Without it the API cannot
    /// correlate dimensions to the package and returns WEIGHT_OR_DIMENSIONS_OR_VOLUME_REQUIRED.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "1";

    /// <summary>Gross weight in <b>grams</b> (Bring Shipping Guide 2.0 field name is "grossWeight").</summary>
    [JsonPropertyName("grossWeight")]
    public decimal GrossWeight { get; set; }

    /// <summary>Length in cm (Bring field name is "length", no "InCm" suffix).</summary>
    [JsonPropertyName("length")]
    public decimal Length { get; set; }

    /// <summary>Width in cm (Bring field name is "width", no "InCm" suffix).</summary>
    [JsonPropertyName("width")]
    public decimal Width { get; set; }

    /// <summary>Height in cm (Bring field name is "height", no "InCm" suffix).</summary>
    [JsonPropertyName("height")]
    public decimal Height { get; set; }
}

// ── Response DTOs ────────────────────────────────────────────────────────────

internal class BringShipmentResponse
{
    [JsonPropertyName("consignments")]
    public List<BringConsignmentResult>? Consignments { get; set; }
}

internal class BringConsignmentResult
{
    [JsonPropertyName("products")]
    public List<BringProductResult>? Products { get; set; }
}

internal class BringProductResult
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("guiInformation")]
    public BringGuiInformation? GuiInformation { get; set; }

    [JsonPropertyName("price")]
    public BringPrice? Price { get; set; }

    [JsonPropertyName("expectedDelivery")]
    public BringExpectedDelivery? ExpectedDelivery { get; set; }

    [JsonPropertyName("errors")]
    public List<BringError>? Errors { get; set; }
}

internal class BringGuiInformation
{
    [JsonPropertyName("productName")]
    public string? ProductName { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}

internal class BringPrice
{
    [JsonPropertyName("listPrice")]
    public BringListPrice? ListPrice { get; set; }
}

internal class BringListPrice
{
    [JsonPropertyName("priceWithoutAdditionalServices")]
    public BringPriceAmount? PriceWithoutAdditionalServices { get; set; }
}

internal class BringPriceAmount
{
    /// <summary>Amount including VAT, as decimal string.</summary>
    [JsonPropertyName("amountWithVAT")]
    public string? AmountWithVAT { get; set; }

    /// <summary>Amount excluding VAT, as decimal string.</summary>
    [JsonPropertyName("amountWithoutVAT")]
    public string? AmountWithoutVAT { get; set; }

    [JsonPropertyName("currencyIdentificationCode")]
    public string? CurrencyIdentificationCode { get; set; }
}

internal class BringExpectedDelivery
{
    [JsonPropertyName("workingDays")]
    public int? WorkingDays { get; set; }

    [JsonPropertyName("formattedExpectedDeliveryDate")]
    public string? FormattedExpectedDeliveryDate { get; set; }
}

internal class BringError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>Machine-readable error code returned alongside <see cref="Code"/>.</summary>
    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
