using BannerShop.Core.Entities;
using BannerShop.Core.Enums;

namespace BannerShop.Api.Services;

public interface IPricingService
{
    /// <summary>
    /// Compute the base banner price (excluding eyelet addon) for the given pricing rule
    /// applied to a specific (width, height) banner.
    ///
    /// BANNERSH-255: the rule now is a range-based row defining
    /// <see cref="BannerSize.PricingHeightCm"/> and
    /// <see cref="BannerSize.PricingMultiplier"/>; the actual dimensions come from
    /// the caller (no more <c>IsCustomWidth/IsCustomHeight</c> branches).
    /// </summary>
    Task<decimal> CalculatePriceAsync(BannerSize rule, int widthCm, int heightCm);

    /// <summary>
    /// Finds the cheapest matching pricing rule for the given banner dimensions
    /// (and optionally a material).  Returns <c>null</c> when no rule covers the
    /// requested dimensions.
    /// </summary>
    Task<PriceMatch?> FindCheapestAsync(int widthCm, int heightCm, int? materialId = null, CancellationToken ct = default);

    /// <summary>
    /// Calculate the total eyelet (malje) addon fee for a single banner of the given dimensions.
    /// Returns 0 when <paramref name="option"/> is <see cref="EyeletOption.None"/>.
    /// </summary>
    Task<(decimal FeeNok, int Count)> CalculateEyeletCostAsync(int widthCm, int heightCm, EyeletOption option);

    /// <summary>Price per individual eyelet (malje) in NOK, read from pricing parameters.</summary>
    Task<decimal> GetEyeletPriceNokAsync();
}

/// <summary>Result of <see cref="IPricingService.FindCheapestAsync"/> — the winning rule + its computed price.</summary>
public sealed record PriceMatch(BannerSize Rule, decimal PriceNok);
