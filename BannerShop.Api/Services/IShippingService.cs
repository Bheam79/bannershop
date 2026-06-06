namespace BannerShop.Api.Services;

/// <summary>
/// Dimensions of a single shipping parcel after rolling the banner(s).
/// All measurements in metric units.
/// </summary>
public record ParcelDimensions(decimal LengthCm, decimal WidthCm, decimal HeightCm, decimal WeightKg);

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
