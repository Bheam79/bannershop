using BannerShop.Api.Services.Shipping;
using BannerShop.Core.Entities;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests;

public class ParcelCalculatorTests
{
    private static ParcelCalculator CreateSeeded(out BannerShop.Infrastructure.Data.BannerShopDbContext db)
    {
        db = DbHelper.CreateInMemory();
        DbHelper.SeedPricingParameters(db);
        return new ParcelCalculator(db);
    }

    // ── Tube length ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Calculate_StandardSize_TubeLengthEqualsBannerWidth()
    {
        // 300cm banner width < 240cm max → tube = 240 (capped)
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial(weightGsm: 400);
        var size = DbHelper.MakeStandardSize(1, 300, 150, material);

        var parcel = await calc.CalculateAsync(size, null, 1);

        // 300cm > 240cm max → capped at 240
        parcel.LengthCm.Should().Be(240m);
    }

    [Fact]
    public async Task Calculate_SmallSize_TubeLengthEqualsBannerWidth()
    {
        // 100cm < 240cm → not capped
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 100, 150, material);

        var parcel = await calc.CalculateAsync(size, null, 1);

        parcel.LengthCm.Should().Be(100m);
    }

    [Fact]
    public async Task Calculate_BannerWidthExceedsMaxLength_TubeLengthCapped()
    {
        // 500cm > 240cm max → capped at 240
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 500, 150, material);

        var parcel = await calc.CalculateAsync(size, null, 1);

        parcel.LengthCm.Should().Be(240m);
    }

    // ── Cross-section (qty scaling) ──────────────────────────────────────────

    [Fact]
    public async Task Calculate_SingleQuantity_CrossSectionEqualsBaseDiameter()
    {
        // qty=1: ceil(15 × sqrt(1)) = ceil(15) = 15
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 200, 150, material);

        var parcel = await calc.CalculateAsync(size, null, 1);

        parcel.WidthCm.Should().Be(15m);
        parcel.HeightCm.Should().Be(15m);
    }

    [Fact]
    public async Task Calculate_FourQuantity_CrossSectionDoubles()
    {
        // qty=4: ceil(15 × sqrt(4)) = ceil(15 × 2) = 30
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 200, 150, material);

        var parcel = await calc.CalculateAsync(size, null, 4);

        parcel.WidthCm.Should().Be(30m);
        parcel.HeightCm.Should().Be(30m);
    }

    [Fact]
    public async Task Calculate_ZeroOrNegativeQty_TreatedAsOne()
    {
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 200, 150, material);

        var parcelZero = await calc.CalculateAsync(size, null, 0);
        var parcelOne  = await calc.CalculateAsync(size, null, 1);

        parcelZero.WeightKg.Should().Be(parcelOne.WeightKg);
        parcelZero.WidthCm.Should().Be(parcelOne.WidthCm);
    }

    // ── Weight calculation ───────────────────────────────────────────────────

    [Fact]
    public async Task Calculate_WeightIncludesMaterialGsmAndPackaging()
    {
        // 200×150 cm = 3.0sqm; gsm=400; contents=400×3.0=1200g; + 500g packaging = 1700g = 1.70kg
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial(weightGsm: 400);
        var size = DbHelper.MakeStandardSize(1, 200, 150, material);

        var parcel = await calc.CalculateAsync(size, null, 1);

        parcel.WeightKg.Should().Be(1.70m);
    }

    [Fact]
    public async Task Calculate_MultipleQty_WeightScalesWithQty()
    {
        // 200×150 = 3.0sqm; gsm=400; qty=2; contents=400×3.0×2=2400g; +500 packaging = 2900g = 2.90kg
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial(weightGsm: 400);
        var size = DbHelper.MakeStandardSize(1, 200, 150, material);

        var parcel = await calc.CalculateAsync(size, null, 2);

        parcel.WeightKg.Should().Be(2.90m);
    }

    // ── Custom width ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Calculate_CustomSizeWithWidth_UsesProvidedWidth()
    {
        // customWidthCm=180: 180×150 = 2.7sqm; gsm=400; 400×2.7=1080g; +500=1580g = 1.58kg
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial(widthCm: 160, weightGsm: 400);
        var size = DbHelper.MakeCustomWidthSize(1, 150, material);

        var parcel = await calc.CalculateAsync(size, customWidthCm: 180, qty: 1);

        parcel.WeightKg.Should().Be(1.58m);
    }

    [Fact]
    public async Task Calculate_CustomSizeWithoutWidth_FallsBackToMaterialWidth()
    {
        // No customWidthCm → uses material.WidthCm=160
        // 160×150 = 2.4sqm; gsm=400; 400×2.4=960g; +500=1460g = 1.46kg
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial(widthCm: 160, weightGsm: 400);
        var size = DbHelper.MakeCustomWidthSize(1, 150, material);

        var parcel = await calc.CalculateAsync(size, customWidthCm: null, qty: 1);

        parcel.WeightKg.Should().Be(1.46m);
    }

    [Fact]
    public async Task Calculate_StandardSizeWithNullWidthCm_ThrowsInvalidOperation()
    {
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial();
        var size = new BannerSize
        {
            Id = 99, WidthCm = null, HeightCm = 150, IsCustomWidth = false,
            Name = "Bad", IsActive = true, MaterialId = material.Id, Material = material
        };

        var act = () => calc.CalculateAsync(size, null, 1);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
