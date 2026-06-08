using BannerShop.Api.Services;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests;

public class PricingServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (PricingService service, BannerShop.Infrastructure.Data.BannerShopDbContext db) CreateSeeded()
    {
        var db = DbHelper.CreateInMemory();
        DbHelper.SeedPricingParameters(db);
        return (new PricingService(db), db);
    }

    // ── Fixed price sizes ────────────────────────────────────────────────────

    [Fact]
    public async Task CalculatePrice_FixedPriceSize_ReturnsFixedPriceWithoutDbLookup()
    {
        // No pricing params seeded → if formula runs it would return default values.
        // The fixed-price path must return early.
        var db = DbHelper.CreateInMemory();
        var service = new PricingService(db);

        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 300, 180, material, fixedPrice: 699m);

        var price = await service.CalculatePriceAsync(size);

        price.Should().Be(699m);
    }

    [Fact]
    public async Task CalculatePrice_FixedPriceSize_IgnoresCustomWidthCm()
    {
        var db = DbHelper.CreateInMemory();
        var service = new PricingService(db);

        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 300, 180, material, fixedPrice: 499m);

        // customWidthCm should be ignored for fixed-price sizes
        var price = await service.CalculatePriceAsync(size, customWidthCm: 999);

        price.Should().Be(499m);
    }

    // ── Standard sizes (formula-based) ───────────────────────────────────────

    [Fact]
    public async Task CalculatePrice_StandardSizeLargeArea_ReturnsAreaTimesBasePricePerSqm()
    {
        // 300cm × 150cm = 4.5 sqm; 4.5 × 180 = 810 > minimum 399
        var (service, _) = CreateSeeded();
        var material = DbHelper.MakeMaterial(weightGsm: 400);
        var size = DbHelper.MakeStandardSize(1, 300, 150, material);

        var price = await service.CalculatePriceAsync(size);

        price.Should().Be(810m);
    }

    [Fact]
    public async Task CalculatePrice_StandardSizeSmallArea_ReturnsMinimumPrice()
    {
        // 50cm × 50cm = 0.25 sqm; 0.25 × 180 = 45 < minimum 399 → returns 399
        var (service, _) = CreateSeeded();
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 50, 50, material);

        var price = await service.CalculatePriceAsync(size);

        price.Should().Be(399m);
    }

    [Fact]
    public async Task CalculatePrice_StandardSizeExactlyAtMinimum_ReturnsMinimumPrice()
    {
        // ~150cm × 148cm ≈ 2.22sqm; 2.22 × 180 = 399.6 ≥ 399 — result is formula price
        // But with 100cm × 100cm = 1sqm; 1 × 180 = 180 < 399 → minimum
        var (service, _) = CreateSeeded();
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 100, 100, material);

        var price = await service.CalculatePriceAsync(size);

        price.Should().Be(399m);
    }

    // ── Eyelet (malje) addon ─────────────────────────────────────────────────────

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
        db.PricingParameters.AddRange(
            new PricingParameter { Id = 4, Name = "eyelet", Key = "eyelet_price_nok", Value = 10m }
        );
        db.SaveChanges();

        var service = new PricingService(db);
        var (fee, count) = await service.CalculateEyeletCostAsync(300, 150, EyeletOption.FourCorners);

        count.Should().Be(4);
        fee.Should().Be(40m); // 4 × 10
    }

    [Fact]
    public async Task CalculateEyeletCost_PerMeter_300x150_Returns10Eyelets()
    {
        // 300cm width: 2 intermediates per side (at 100 and 200) → top+bottom = 4
        // 150cm height: 1 intermediate per side (at 75) → left+right = 2
        // Total: 4 corners + 4 + 2 = 10 eyelets
        var db = DbHelper.CreateInMemory();
        db.PricingParameters.Add(new PricingParameter { Id = 4, Name = "eyelet", Key = "eyelet_price_nok", Value = 15m });
        db.SaveChanges();

        var service = new PricingService(db);
        var (fee, count) = await service.CalculateEyeletCostAsync(300, 150, EyeletOption.PerMeter);

        count.Should().Be(10);
        fee.Should().Be(150m); // 10 × 15
    }

    [Fact]
    public async Task CalculateEyeletCost_PerMeter_ZeroPriceParam_ReturnsZeroFeeNonZeroCount()
    {
        // If admin hasn't set a price yet (0m), count is computed but fee is 0.
        var (service, _) = CreateSeeded(); // seeded eyelet_price_nok = 0m

        var (fee, count) = await service.CalculateEyeletCostAsync(300, 150, EyeletOption.PerMeter);

        count.Should().Be(10);
        fee.Should().Be(0m);
    }

    [Fact]
    public async Task CalculatePrice_UsesPerMaterialPricePerSqm_NotGlobalParameter()
    {
        // Global base_price_per_sqm = 180; material has PricePerSqm = 140 (e.g. 680g heavy-duty).
        // 300cm × 150cm = 4.5 sqm; 4.5 × 140 = 630 > minimum 399 → result must be 630, not 810.
        var (service, _) = CreateSeeded(); // global seeded with 180 NOK/m²
        var material = DbHelper.MakeMaterial(pricePerSqm: 140m); // diverges from global
        var size = DbHelper.MakeStandardSize(1, 300, 150, material);

        var price = await service.CalculatePriceAsync(size);

        price.Should().Be(630m); // 4.5 × 140 = 630
    }

    [Fact]
    public async Task CalculatePrice_MissingMaterialNavigation_FallsBackToGlobalParameter()
    {
        // When Material is null (caller forgot .Include), the service falls back to the
        // global base_price_per_sqm parameter (180 NOK/m²) so it never crashes.
        var (service, _) = CreateSeeded();
        var size = new BannerSize
        {
            Id = 1,
            WidthCm = 300,
            HeightCm = 150,
            IsCustomWidth = false,
            Name = "300 × 150 cm",
            IsActive = true,
            MaterialId = 1,
            Material = null! // navigation not loaded
        };

        var price = await service.CalculatePriceAsync(size);

        // Falls back to global 180 NOK/m²: 4.5 × 180 = 810
        price.Should().Be(810m);
    }

    // ── Custom-width sizes ───────────────────────────────────────────────────

    [Fact]
    public async Task CalculatePrice_CustomWidthWithWidth_IncludesCustomSurcharge()
    {
        // 200cm × 150cm = 3.0sqm; 3.0 × 180 = 540 > 399; + 150 surcharge = 690
        var (service, _) = CreateSeeded();
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeCustomWidthSize(1, 150, material);

        var price = await service.CalculatePriceAsync(size, customWidthCm: 200);

        price.Should().Be(690m);
    }

    [Fact]
    public async Task CalculatePrice_CustomWidthWithSmallWidth_ReturnsMinimumPlusSurcharge()
    {
        // 50cm × 150cm = 0.75sqm; 0.75 × 180 = 135 < 399 → use min 399; + 150 surcharge = 549
        var (service, _) = CreateSeeded();
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeCustomWidthSize(1, 150, material);

        var price = await service.CalculatePriceAsync(size, customWidthCm: 50);

        price.Should().Be(549m);
    }

    [Fact]
    public async Task CalculatePrice_CustomWidthWithoutWidth_ReturnsMinimumPlusSurcharge()
    {
        // No customWidthCm → widthCm = 0 → base = minimum_price; + surcharge = 399 + 150 = 549
        var (service, _) = CreateSeeded();
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeCustomWidthSize(1, 150, material);

        var price = await service.CalculatePriceAsync(size, customWidthCm: null);

        price.Should().Be(549m);
    }

    // ── BANNERSH-88: multi-panel multiplier ──────────────────────────────────

    [Fact]
    public async Task CalculatePrice_BannerWiderThanMaxButWithin2x_DoublesThePrice()
    {
        // Material max 160 cm, overlap 5 cm → 2 panels fit widths up to 2·160 − 5 = 315 cm.
        // 300×150 = 4.5 sqm × 180 = 810 NOK base; ×2 panels = 1620 NOK.
        var (service, _) = CreateSeeded();
        var material = DbHelper.MakeMaterial(maxBannerWidthCm: 160);
        var size = DbHelper.MakeStandardSize(1, 300, 150, material);

        var price = await service.CalculatePriceAsync(size);

        price.Should().Be(1620m);
    }

    [Fact]
    public async Task CalculatePrice_BannerExactlyAt2xBoundary_DoublesThePrice()
    {
        // width = 2·M − overlap = 2·160 − 5 = 315 cm → exactly 2 panels.
        // 315×150 = 4.725 sqm × 180 = 850.5 NOK base × 2 = 1701 NOK.
        var (service, _) = CreateSeeded();
        var material = DbHelper.MakeMaterial(maxBannerWidthCm: 160);
        var size = DbHelper.MakeCustomWidthSize(1, 150, material);

        var price = await service.CalculatePriceAsync(size, customWidthCm: 315);

        // base = 4.725 × 180 = 850.5; + 150 surcharge = 1000.5; × 2 = 2001
        price.Should().Be(2001m);
    }

    [Fact]
    public async Task CalculatePrice_BannerJustOver2xBoundary_TriplesThePrice()
    {
        // width 316 > 2·160 − 5 = 315 → 3 panels needed.
        var (service, _) = CreateSeeded();
        var material = DbHelper.MakeMaterial(maxBannerWidthCm: 160);
        var size = DbHelper.MakeCustomWidthSize(1, 150, material);

        var price = await service.CalculatePriceAsync(size, customWidthCm: 316);

        // base = (316/100) × (150/100) × 180 = 853.2; + 150 = 1003.2; × 3 = 3009.6
        price.Should().Be(3009.60m);
    }

    [Fact]
    public async Task CalculatePrice_BannerWidthAtOrBelowMax_NoMultiplier()
    {
        // 160 cm banner on 160 cm max → 1 panel.
        var (service, _) = CreateSeeded();
        var material = DbHelper.MakeMaterial(maxBannerWidthCm: 160);
        var size = DbHelper.MakeStandardSize(1, 160, 150, material);

        var price = await service.CalculatePriceAsync(size);

        // 1.6 × 1.5 × 180 = 432, ≥ 399 minimum, ×1 = 432
        price.Should().Be(432m);
    }

    [Fact]
    public async Task CalculatePrice_FixedPriceSize_IgnoresPanelMultiplier()
    {
        // Fixed-price sizes skip the formula AND the multiplier.
        var (service, _) = CreateSeeded();
        var material = DbHelper.MakeMaterial(maxBannerWidthCm: 160);
        var size = DbHelper.MakeStandardSize(1, 400, 150, material, fixedPrice: 699m);

        var price = await service.CalculatePriceAsync(size);

        price.Should().Be(699m);
    }

    [Fact]
    public async Task CalculatePrice_MissingMaterialNavigation_DoesNotApplyMultiplier()
    {
        // Defensive: if a caller forgets to Include(s => s.Material), we should not crash
        // and not retroactively triple legacy prices.
        var (service, _) = CreateSeeded();
        var size = new BannerSize
        {
            Id = 1,
            WidthCm = 500,
            HeightCm = 150,
            IsCustomWidth = false,
            Name = "500 × 150 cm",
            IsActive = true,
            MaterialId = 1,
            Material = null! // navigation not loaded
        };

        var price = await service.CalculatePriceAsync(size);

        // 5 × 1.5 × 180 = 1350; ×1 = 1350
        price.Should().Be(1350m);
    }

    [Theory]
    // (bannerWidth, maxPerPanel, overlap, expectedPanels)
    [InlineData(100, 160, 5, 1)]   // smaller than max → 1
    [InlineData(160, 160, 5, 1)]   // exactly at max → 1
    [InlineData(161, 160, 5, 2)]   // just over max → 2
    [InlineData(315, 160, 5, 2)]   // 2·160 − 5 boundary → 2
    [InlineData(316, 160, 5, 3)]   // just over 2x boundary → 3
    [InlineData(470, 160, 5, 3)]   // 3·160 − 2·5 boundary → 3
    [InlineData(471, 160, 5, 4)]   // just over 3-panel boundary → 4
    [InlineData(500, 180, 0, 3)]   // material 180, zero overlap, 500/180 → 3
    [InlineData(0,   160, 5, 1)]   // degenerate width → 1
    [InlineData(300, 0,   5, 1)]   // degenerate max → 1 (defensive)
    public void PanelsNeeded_ReturnsExpected(int bannerWidth, int maxPerPanel, int overlap, int expected)
    {
        PricingService.PanelsNeeded(bannerWidth, maxPerPanel, overlap).Should().Be(expected);
    }

    [Fact]
    public async Task CalculatePrice_StandardSize_ThrowsWhenWidthCmIsNull()
    {
        var (service, _) = CreateSeeded();
        var material = DbHelper.MakeMaterial();
        // A non-custom size with null WidthCm is a data error
        var size = new BannerSize
        {
            Id = 1,
            WidthCm = null,
            HeightCm = 150,
            IsCustomWidth = false,
            Name = "Bad size",
            IsActive = true,
            MaterialId = material.Id,
            Material = material
        };

        var act = () => service.CalculatePriceAsync(size);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
