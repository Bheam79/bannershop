using BannerShop.Api.Services;
using BannerShop.Core.Entities;
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

    [Fact]
    public async Task CalculatePrice_StandardSize_HemFeeAddedWhenNonZero()
    {
        var db = DbHelper.CreateInMemory();
        // Override hem fee to 50
        db.PricingParameters.AddRange(
            new PricingParameter { Id = 1, Name = "base", Key = "base_price_per_sqm",       Value = 180m },
            new PricingParameter { Id = 2, Name = "min",  Key = "minimum_price",             Value = 399m },
            new PricingParameter { Id = 3, Name = "sur",  Key = "custom_width_surcharge",    Value = 150m },
            new PricingParameter { Id = 4, Name = "hem",  Key = "hem_and_eyelets_flat_fee",  Value = 50m  }
        );
        db.SaveChanges();

        var service = new PricingService(db);
        var material = DbHelper.MakeMaterial();
        // 300×150 = 4.5sqm; 4.5×180 = 810; + 50 hem = 860
        var size = DbHelper.MakeStandardSize(1, 300, 150, material);

        var price = await service.CalculatePriceAsync(size);

        price.Should().Be(860m);
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
