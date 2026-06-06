using BannerShop.Core.Entities;

namespace BannerShop.Api.Services;

public interface IPricingService
{
    /// <summary>
    /// Calculate price for a banner size.
    /// For custom-width sizes, pass <paramref name="customWidthCm"/> to get an accurate quote;
    /// when omitted the minimum price is used as a lower-bound estimate.
    /// </summary>
    Task<decimal> CalculatePriceAsync(BannerSize size, int? customWidthCm = null);
}
