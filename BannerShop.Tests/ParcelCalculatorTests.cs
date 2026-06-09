using BannerShop.Api.Services.Shipping;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
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

    // ─────────────────────────────────────────────────────────────────────────
    // Rolled mode (BANNERSH-143): L = shortest + 2 cm, W = H = (9 + 0.5 × long_m) × √qty
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rolled_StandardSize_TubeLengthEqualsShortestSidePlus2cm()
    {
        // 300×150 banner — shortest = 150 → tube length = 152 cm
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 300, 150, material);

        var parcel = await calc.CalculateAsync(size, null, 1, PackingMode.Rolled);

        parcel.LengthCm.Should().Be(152m);
    }

    [Fact]
    public async Task Rolled_TallBanner_TubeLengthUsesShortestSide()
    {
        // 100×150 banner — shortest = 100 → tube length = 102 cm
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 100, 150, material);

        var parcel = await calc.CalculateAsync(size, null, 1, PackingMode.Rolled);

        parcel.LengthCm.Should().Be(102m);
    }

    [Fact]
    public async Task Rolled_VeryWideBanner_TubeLengthStillUsesShortestSide()
    {
        // 500×150 — shortest = 150 → length = 152, NOT capped (the banner rolls
        // along its long axis so length tracks the SHORT side regardless of
        // overall banner width).
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 500, 150, material);

        var parcel = await calc.CalculateAsync(size, null, 1, PackingMode.Rolled);

        parcel.LengthCm.Should().Be(152m);
    }

    [Fact]
    public async Task Rolled_CrossSection_ScalesWithLongSide()
    {
        // 300×150 — long = 3.0 m → cross = 9 + 0.5×3 = 10.5 cm
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 300, 150, material);

        var parcel = await calc.CalculateAsync(size, null, 1, PackingMode.Rolled);

        parcel.WidthCm.Should().Be(10.5m);
        parcel.HeightCm.Should().Be(10.5m);
    }

    [Fact]
    public async Task Rolled_SmallBanner_CrossSectionUsesBase()
    {
        // 100×150 — long = 1.5 m → cross = 9 + 0.5×1.5 = 9.75 cm (rounded to 9.8)
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 100, 150, material);

        var parcel = await calc.CalculateAsync(size, null, 1, PackingMode.Rolled);

        parcel.WidthCm.Should().Be(9.8m);
        parcel.HeightCm.Should().Be(9.8m);
    }

    [Fact]
    public async Task Rolled_MultipleQuantity_CrossSectionScalesBySqrtQty()
    {
        // 300×150, qty=4 — per-item cross = 10.5; total cross = 10.5 × √4 = 21.0
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 300, 150, material);

        var parcel = await calc.CalculateAsync(size, null, 4, PackingMode.Rolled);

        parcel.WidthCm.Should().Be(21m);
        parcel.HeightCm.Should().Be(21m);
    }

    [Fact]
    public async Task Rolled_ZeroOrNegativeQty_TreatedAsOne()
    {
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 200, 150, material);

        var parcelZero = await calc.CalculateAsync(size, null, 0, PackingMode.Rolled);
        var parcelOne  = await calc.CalculateAsync(size, null, 1, PackingMode.Rolled);

        parcelZero.WeightKg.Should().Be(parcelOne.WeightKg);
        parcelZero.WidthCm.Should().Be(parcelOne.WidthCm);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Folded mode (BANNERSH-143): 50 × 60 cm footprint, H = (10 + 1 × long_m) × qty
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Folded_AnySize_FootprintIs50x60cm()
    {
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 300, 150, material);

        var parcel = await calc.CalculateAsync(size, null, 1, PackingMode.Folded);

        parcel.WidthCm.Should().Be(50m);
        parcel.LengthCm.Should().Be(60m);
    }

    [Fact]
    public async Task Folded_Height_ScalesWithLongSide()
    {
        // 300×150 — long = 3.0 m → height = 10 + 1×3 = 13 cm
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 300, 150, material);

        var parcel = await calc.CalculateAsync(size, null, 1, PackingMode.Folded);

        parcel.HeightCm.Should().Be(13m);
    }

    [Fact]
    public async Task Folded_MultipleQty_HeightStacksLinearly()
    {
        // 300×150, qty=3 — per-item height = 13 cm; stacked = 39 cm
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 300, 150, material);

        var parcel = await calc.CalculateAsync(size, null, 3, PackingMode.Folded);

        parcel.HeightCm.Should().Be(39m);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Weight (shared by both packing modes)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Weight_IncludesMaterialGsmAndPackaging()
    {
        // 200×150 cm = 3.0sqm; gsm=400; contents=400×3.0=1200g; + 500g packaging = 1700g = 1.70kg
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial(weightGsm: 400);
        var size = DbHelper.MakeStandardSize(1, 200, 150, material);

        var parcel = await calc.CalculateAsync(size, null, 1, PackingMode.Rolled);

        parcel.WeightKg.Should().Be(1.70m);
    }

    [Fact]
    public async Task Weight_MultipleQty_ScalesWithQty()
    {
        // 200×150 = 3.0sqm; gsm=400; qty=2; contents=400×3.0×2=2400g; +500 packaging = 2900g = 2.90kg
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial(weightGsm: 400);
        var size = DbHelper.MakeStandardSize(1, 200, 150, material);

        var parcel = await calc.CalculateAsync(size, null, 2, PackingMode.Rolled);

        parcel.WeightKg.Should().Be(2.90m);
    }

    [Fact]
    public async Task Weight_Folded_IsIdenticalToRolled()
    {
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial(weightGsm: 400);
        var size = DbHelper.MakeStandardSize(1, 200, 150, material);

        var rolled = await calc.CalculateAsync(size, null, 1, PackingMode.Rolled);
        var folded = await calc.CalculateAsync(size, null, 1, PackingMode.Folded);

        folded.WeightKg.Should().Be(rolled.WeightKg);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Custom width
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CustomSize_WithWidth_UsesProvidedWidth()
    {
        // customWidthCm=180: 180×150 = 2.7sqm; gsm=400; 400×2.7=1080g; +500=1580g = 1.58kg
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial(widthCm: 160, weightGsm: 400);
        var size = DbHelper.MakeCustomWidthSize(1, 150, material);

        var parcel = await calc.CalculateAsync(size, customWidthCm: 180, qty: 1, PackingMode.Rolled);

        parcel.WeightKg.Should().Be(1.58m);
    }

    [Fact]
    public async Task CustomSize_WithoutWidth_FallsBackToMaterialWidth()
    {
        // No customWidthCm → uses material.WidthCm=160
        // 160×150 = 2.4sqm; gsm=400; 400×2.4=960g; +500=1460g = 1.46kg
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial(widthCm: 160, weightGsm: 400);
        var size = DbHelper.MakeCustomWidthSize(1, 150, material);

        var parcel = await calc.CalculateAsync(size, customWidthCm: null, qty: 1, PackingMode.Rolled);

        parcel.WeightKg.Should().Be(1.46m);
    }

    [Fact]
    public async Task StandardSize_WithNullWidthCm_ThrowsInvalidOperation()
    {
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial();
        var size = new BannerSize
        {
            Id = 99, WidthCm = null, HeightCm = 150, IsCustomWidth = false,
            Name = "Bad", IsActive = true, MaterialId = material.Id, Material = material
        };

        var act = () => calc.CalculateAsync(size, null, 1, PackingMode.Rolled);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Default packing mode (no enum argument) is Rolled
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DefaultOverload_UsesRolledMode()
    {
        var calc = CreateSeeded(out var db);
        var material = DbHelper.MakeMaterial();
        var size = DbHelper.MakeStandardSize(1, 300, 150, material);

        var defaultParcel = await calc.CalculateAsync(size, null, 1);
        var rolledParcel  = await calc.CalculateAsync(size, null, 1, PackingMode.Rolled);

        defaultParcel.LengthCm.Should().Be(rolledParcel.LengthCm);
        defaultParcel.WidthCm.Should().Be(rolledParcel.WidthCm);
        defaultParcel.HeightCm.Should().Be(rolledParcel.HeightCm);
        defaultParcel.WeightKg.Should().Be(rolledParcel.WeightKg);
    }
}
