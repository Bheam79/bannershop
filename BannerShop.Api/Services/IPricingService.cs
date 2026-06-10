using BannerShop.Core.Entities;
using BannerShop.Core.Enums;

namespace BannerShop.Api.Services;

public interface IPricingService
{
    /// <summary>
    /// Calculate the base banner price (excluding optional eyelet addon).
    /// For custom-width sizes, pass <paramref name="customWidthCm"/> to get an accurate quote;
    /// for custom-height sizes, pass <paramref name="customHeightCm"/>;
    /// when omitted the minimum price is used as a lower-bound estimate.
    /// Set <paramref name="skipCustomSurcharge"/> to <c>true</c> to omit the
    /// <c>custom_width_surcharge</c> fee — used when dimensions are derived
    /// automatically (e.g. the AI quality-picker) rather than explicitly
    /// requested as a custom size.
    /// </summary>
    Task<decimal> CalculatePriceAsync(BannerSize size, int? customWidthCm = null, int? customHeightCm = null, bool skipCustomSurcharge = false);

    /// <summary>
    /// Calculate the total eyelet (malje) addon fee for a single banner of the given dimensions.
    /// Returns 0 when <paramref name="option"/> is <see cref="EyeletOption.None"/>.
    /// </summary>
    Task<(decimal FeeNok, int Count)> CalculateEyeletCostAsync(int widthCm, int heightCm, EyeletOption option);

    /// <summary>Price per individual eyelet (malje) in NOK, read from pricing parameters.</summary>
    Task<decimal> GetEyeletPriceNokAsync();
}
