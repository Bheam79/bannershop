namespace BannerShop.Api.Services;

/// <summary>
/// Dimensions of a single shipping parcel after rolling the banner(s).
/// All measurements in metric units.
/// </summary>
/// <param name="LengthCm">Length of the parcel in centimetres.</param>
/// <param name="WidthCm">Width of the parcel in centimetres.</param>
/// <param name="HeightCm">Height of the parcel in centimetres.</param>
/// <param name="WeightKg">Gross weight in kilograms.</param>
/// <param name="NonStackable">
/// When <c>true</c> the package cannot be stacked (e.g. a cylindrical tube).
/// Passed to Bring as <c>nonStackable: true</c>.
/// </param>
/// <param name="VolumeSpecial">
/// When <c>true</c> Bring will adjust the price for special volume.
/// Per the Bring Shipping Guide 2.0 API docs this flag is specifically
/// "typically used for roll or other weirdly shaped packages" — i.e. cylinders/tubes
/// where the effective volume differs from a rectangular bounding box.
/// This is the primary flag that triggers Bring's tube-pricing surcharge.
/// </param>
public record ParcelDimensions(
    decimal LengthCm,
    decimal WidthCm,
    decimal HeightCm,
    decimal WeightKg,
    bool NonStackable = false,
    bool VolumeSpecial = false);

/// <summary>
/// A shipping option returned for a particular carrier service.
/// </summary>
public record ShippingOption(decimal CostNok, int EstimatedDays, string? CarrierProductId, string? CarrierProductName);

/// <summary>
/// Combined shipping quote for both standard and express delivery.
/// Express uses the same shipping cost; the express production fee is added on top
/// (handled by the caller / pricing layer, not here).
/// </summary>
public record ShippingQuote(ShippingOption Standard, ShippingOption Express);

/// <summary>
/// Calculates shipping costs for parcels via an external carrier (Bring/Posten).
/// </summary>
public interface IShippingService
{
    /// <summary>
    /// Calculate a shipping quote for a parcel sent from the configured sender postcode
    /// to <paramref name="toPostalCode"/> in Norway.
    /// </summary>
    /// <exception cref="ShippingUnavailableException">
    /// Thrown when the carrier is unreachable, returns an error, or no valid product price is found.
    /// </exception>
    Task<ShippingQuote> CalculateAsync(
        string toPostalCode,
        string? toCity,
        ParcelDimensions parcel,
        CancellationToken ct = default);
}

/// <summary>
/// Thrown when the shipping carrier API is unavailable or returns no usable result.
/// The controller maps this to a 503 response so the customer is told to contact the shop.
/// </summary>
public class ShippingUnavailableException : Exception
{
    public ShippingUnavailableException(string message) : base(message) { }
    public ShippingUnavailableException(string message, Exception inner) : base(message, inner) { }
}
