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

    [JsonPropertyName("products")]
    public List<BringProductRef> Products { get; set; } = new();

    [JsonPropertyName("packages")]
    public List<BringPackage> Packages { get; set; } = new();
}

internal class BringProductRef
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

internal class BringPackage
{
    [JsonPropertyName("weightInKg")]
    public decimal WeightInKg { get; set; }

    [JsonPropertyName("lengthInCm")]
    public decimal LengthInCm { get; set; }

    [JsonPropertyName("widthInCm")]
    public decimal WidthInCm { get; set; }

    [JsonPropertyName("heightInCm")]
    public decimal HeightInCm { get; set; }
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

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
