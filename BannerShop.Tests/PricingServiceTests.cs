using BannerShop.Api.Services;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Tests for <see cref="PricingService"/> against the BANNERSH-255 range-based
/// pricing rules. Each <see cref="BannerSize"/> defines a (width × height)
/// range, a pricing height that overrides the actual height in the formula, and
/// a multiplier (1×, 2×, 3× …) so the admin can encode banner gluing tiers
/// without an external panel calculator.
/// </summary>
public class PricingServiceTests
{
    private static (PricingService service, BannerShop.Infrastructure.Data.BannerShopDbContext db) CreateSeeded()
    {
        var db = DbHelper.CreateInMemory();
        DbHelper.SeedPricingParameters(db);
        return (new PricingService(db), db);
    }

    // ── Fixed price rules ────────────────────────────────────────────────────

    [Fact]
    public async Task CalculatePrice_FixedPriceRule_ReturnsFixedPriceIgnoringDims()
    {
        var db = DbHelper.CreateInMemory();
        var service = new PricingService(db);

        var material = DbHelper.MakeMaterial();
        var rule = DbHelper.MakeSizeRule(1, material, 1, 500, 1, 500, pricingHeight: 150, multiplier: 1, fixedPrice: 699m);

        var price = await service.CalculatePriceAsync(rule, 300, 180);

        price.Should().Be(699m);
    }

    [Fact]
    public async Task CalculatePrice_FixedPriceRule_IgnoresMultiplier()
    {
        // FixedPrice short-circuits the formula AND the multiplier.
        var (service, _) = CreateSeeded();
        var material = DbHelper.MakeMaterial();
        var rule = DbHelper.MakeSizeRule(1, material, 1, 500, 1, 500, pricingHeight: 150, multiplier: 3, fixedPrice: 499m);

        var price = await service.CalculatePriceAsync(rule, 400, 400);

        price.Should().Be(499m);
    }

    // ── Formula-based rules ─────────────────────────────────────────────────

    [Fact]
    public async Task CalculatePrice_StandardRule_AppliesAreaTimesBasePrice()
    {
        // 300 cm × pricingHeight 150 cm × 180 NOK/m² = 810 NOK
        var (service, _) = CreateSeeded();
        var material = DbHelper.MakeMaterial();
        var rule = DbHelper.MakeSizeRule(1, material, 1, 500, 1, 154, pricingHeight: 150, multiplier: 1);

        var price = await service.CalculatePriceAsync(rule, widthCm: 300, heightCm: 100);

        price.Should().Be(810m);
    }

    [Fact]
    public async Task CalculatePrice_SmallBanner_ClampsToMinimum()
    {
        // 50 × 50 cm × 180 NOK/m² = 45 NOK < 399 minimum → 399
        var (service, _) = CreateSeeded();
        var material = DbHelper.MakeMaterial();
        var rule = DbHelper.MakeSizeRule(1, material, 1, 500, 1, 154, pricingHeight: 50, multiplier: 1);

        var price = await service.CalculatePriceAsync(rule, widthCm: 50, heightCm: 50);

        price.Should().Be(399m);
    }

    [Fact]
    public async Task CalculatePrice_PricingHeightOverridesActualHeight()
    {
        // pricingHeight = 154; actualHeight = 100. The customer is charged for 154 cm.
        // (200/100) × (154/100) × 180 = 554.4
        var (service, _) = CreateSeeded();
        var material = DbHelper.MakeMaterial();
        var rule = DbHelper.MakeSizeRule(1, material, 1, 500, 1, 154, pricingHeight: 154, multiplier: 1);

        var price = await service.CalculatePriceAsync(rule, widthCm: 200, heightCm: 100);

        // 2 × 1.54 × 180 = 554.4, > 399 minimum
        price.Should().Be(554.4m);
    }

    [Fact]
    public async Task CalculatePrice_MultiplierApplied()
    {
        // Tier 2 rule (multiplier=2). 300 cm wide × 154 cm pricingHeight × 180 NOK/m² = 831.6 × 2 = 1663.2
        var (service, _) = CreateSeeded();
        var material = DbHelper.MakeMaterial();
        var rule = DbHelper.MakeSizeRule(1, material, 1, 500, 154, 300, pricingHeight: 154, multiplier: 2);

        var price = await service.CalculatePriceAsync(rule, widthCm: 300, heightCm: 200);

        // 3 × 1.54 × 180 = 831.6; × 2 = 1663.2
        price.Should().Be(1663.2m);
    }

    [Fact]
    public async Task CalculatePrice_UsesPerMaterialPricePerSqm()
    {
        // Material with pricePerSqm = 140 instead of global 180.
        var (service, _) = CreateSeeded();
        var material = DbHelper.MakeMaterial(pricePerSqm: 140m);
        var rule = DbHelper.MakeSizeRule(1, material, 1, 500, 1, 200, pricingHeight: 180, multiplier: 1);

        var price = await service.CalculatePriceAsync(rule, widthCm: 300, heightCm: 180);

        // 3 × 1.8 × 140 = 756
        price.Should().Be(756m);
    }

    [Fact]
    public async Task CalculatePrice_MissingMaterialNavigation_FallsBackToGlobalParameter()
    {
        var (service, _) = CreateSeeded();
        var rule = new BannerSize
        {
            Id = 1,
            Name = "no-mat",
            MaterialId = 1,
            MinWidthCm = 1,
            MaxWidthCm = 500,
            MinHeightCm = 1,
            MaxHeightCm = 200,
            PricingHeightCm = 150,
            PricingMultiplier = 1,
            Material = null!
        };

        var price = await service.CalculatePriceAsync(rule, widthCm: 300, heightCm: 150);

        // Falls back to global 180 NOK/m²: 3 × 1.5 × 180 = 810
        price.Should().Be(810m);
    }

    // ── Eyelet (malje) addon ─────────────────────────────────────────────────

    [Fact]
    public async Task CalculateEyeletCost_NoneOption_ReturnsZero()
    {
        var (service, _) = CreateSeeded();

        var (fee, count) = await service.CalculateEyeletCostAsync(300, 150, EyeletOption.None);

        fee.Should().Be(0m);
        count.Should().Be(0);
    }

    [Fact]
    public async Task CalculateEyeletCost_FourCorners_ReturnsFourTimesPrice()
    {
        var db = DbHelper.CreateInMemory();
        db.PricingParameters.Add(new PricingParameter { Id = 4, Name = "eyelet", Key = "eyelet_price_nok", Value = 10m });
        db.SaveChanges();

        var service = new PricingService(db);
        var (fee, count) = await service.CalculateEyeletCostAsync(300, 150, EyeletOption.FourCorners);

        count.Should().Be(4);
        fee.Should().Be(40m);
    }

    // ── FindCheapestAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task FindCheapest_ReturnsCheapestMatchingRule()
    {
        var (service, db) = CreateSeeded();
        var mat = DbHelper.MakeMaterial();
        db.Materials.Add(mat);

        // Two competing rules. Rule A: formula price 810; Rule B: fixedPrice 499.
        db.BannerSizes.AddRange(
            DbHelper.MakeSizeRule(10, mat, 1, 500, 1, 200, pricingHeight: 150, multiplier: 1),
            DbHelper.MakeSizeRule(11, mat, 200, 400, 100, 200, pricingHeight: 150, multiplier: 1, fixedPrice: 499m)
        );
        db.SaveChanges();

        var match = await service.FindCheapestAsync(widthCm: 300, heightCm: 150, materialId: mat.Id);

        match.Should().NotBeNull();
        match!.PriceNok.Should().Be(499m);
        match.Rule.Id.Should().Be(11);
    }

    [Fact]
    public async Task FindCheapest_NoMatch_ReturnsNull()
    {
        var (service, db) = CreateSeeded();
        var mat = DbHelper.MakeMaterial();
        db.Materials.Add(mat);
        db.BannerSizes.Add(DbHelper.MakeSizeRule(10, mat, 1, 100, 1, 100, pricingHeight: 100, multiplier: 1));
        db.SaveChanges();

        // Banner too tall — outside any rule's range.
        var match = await service.FindCheapestAsync(widthCm: 50, heightCm: 500, materialId: mat.Id);

        match.Should().BeNull();
    }

    [Fact]
    public async Task FindCheapest_AcrossAllMaterials_PicksAnyCheapest()
    {
        var (service, db) = CreateSeeded();
        var matA = DbHelper.MakeMaterial(id: 10, pricePerSqm: 200m);
        var matB = DbHelper.MakeMaterial(id: 11, pricePerSqm: 100m);
        db.Materials.AddRange(matA, matB);

        db.BannerSizes.AddRange(
            DbHelper.MakeSizeRule(20, matA, 1, 500, 1, 200, pricingHeight: 150, multiplier: 1),
            DbHelper.MakeSizeRule(21, matB, 1, 500, 1, 200, pricingHeight: 150, multiplier: 1)
        );
        db.SaveChanges();

        var match = await service.FindCheapestAsync(widthCm: 300, heightCm: 150, materialId: null);

        match.Should().NotBeNull();
        match!.Rule.MaterialId.Should().Be(matB.Id); // cheaper material wins
    }
}
