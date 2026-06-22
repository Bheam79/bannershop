using BannerShop.Api.Services.Shipping;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Tests for <see cref="ParcelCalculator"/>. Post-BANNERSH-255 the calculator
/// takes the banner dimensions + material gsm directly (no BannerSize lookup),
/// so each fact passes concrete numbers.
/// </summary>
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
        var calc = CreateSeeded(out _);

        var parcel = await calc.CalculateAsync(widthCm: 300, heightCm: 150, materialWeightGsm: 400, qty: 1, PackingMode.Rolled);

        parcel.LengthCm.Should().Be(152m);
    }

    [Fact]
    public async Task Rolled_TallBanner_TubeLengthUsesShortestSide()
    {
        var calc = CreateSeeded(out _);

        var parcel = await calc.CalculateAsync(widthCm: 100, heightCm: 150, materialWeightGsm: 400, qty: 1, PackingMode.Rolled);

        parcel.LengthCm.Should().Be(102m);
    }

    [Fact]
    public async Task Rolled_VeryWideBanner_TubeLengthStillUsesShortestSide()
    {
        var calc = CreateSeeded(out _);

        var parcel = await calc.CalculateAsync(widthCm: 500, heightCm: 150, materialWeightGsm: 400, qty: 1, PackingMode.Rolled);

        parcel.LengthCm.Should().Be(152m);
    }

    [Fact]
    public async Task Rolled_CrossSection_ScalesWithLongSide()
    {
        var calc = CreateSeeded(out _);

        var parcel = await calc.CalculateAsync(300, 150, 400, 1, PackingMode.Rolled);

        parcel.WidthCm.Should().Be(10.5m);
        parcel.HeightCm.Should().Be(10.5m);
    }

    [Fact]
    public async Task Rolled_SmallBanner_CrossSectionUsesBase()
    {
        var calc = CreateSeeded(out _);

        var parcel = await calc.CalculateAsync(100, 150, 400, 1, PackingMode.Rolled);

        parcel.WidthCm.Should().Be(9.8m);
        parcel.HeightCm.Should().Be(9.8m);
    }

    [Fact]
    public async Task Rolled_MultipleQuantity_CrossSectionScalesBySqrtQty()
    {
        var calc = CreateSeeded(out _);

        var parcel = await calc.CalculateAsync(300, 150, 400, 4, PackingMode.Rolled);

        parcel.WidthCm.Should().Be(21m);
        parcel.HeightCm.Should().Be(21m);
    }

    [Fact]
    public async Task Rolled_ZeroOrNegativeQty_TreatedAsOne()
    {
        var calc = CreateSeeded(out _);

        var parcelZero = await calc.CalculateAsync(200, 150, 400, 0, PackingMode.Rolled);
        var parcelOne  = await calc.CalculateAsync(200, 150, 400, 1, PackingMode.Rolled);

        parcelZero.WeightKg.Should().Be(parcelOne.WeightKg);
        parcelZero.WidthCm.Should().Be(parcelOne.WidthCm);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Folded mode (BANNERSH-143 / BANNERSH-274): 60 × 40 cm footprint, H = (10 + 1 × long_m) × qty
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Folded_AnySize_FootprintIs60x40cm()
    {
        var calc = CreateSeeded(out _);

        var parcel = await calc.CalculateAsync(300, 150, 400, 1, PackingMode.Folded);

        parcel.WidthCm.Should().Be(40m);
        parcel.LengthCm.Should().Be(60m);
    }

    [Fact]
    public async Task Folded_Height_ScalesWithLongSide()
    {
        var calc = CreateSeeded(out _);

        var parcel = await calc.CalculateAsync(300, 150, 400, 1, PackingMode.Folded);

        parcel.HeightCm.Should().Be(13m);
    }

    [Fact]
    public async Task Folded_MultipleQty_HeightStacksLinearly()
    {
        var calc = CreateSeeded(out _);

        var parcel = await calc.CalculateAsync(300, 150, 400, 3, PackingMode.Folded);

        parcel.HeightCm.Should().Be(39m);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Weight (shared by both packing modes)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Weight_IncludesMaterialGsmAndPackaging()
    {
        // 200×150 cm = 3.0sqm; gsm=400; contents=400×3.0=1200g; + 500g packaging = 1700g = 1.70kg
        var calc = CreateSeeded(out _);

        var parcel = await calc.CalculateAsync(200, 150, 400, 1, PackingMode.Rolled);

        parcel.WeightKg.Should().Be(1.70m);
    }

    [Fact]
    public async Task Weight_MultipleQty_ScalesWithQty()
    {
        var calc = CreateSeeded(out _);

        var parcel = await calc.CalculateAsync(200, 150, 400, 2, PackingMode.Rolled);

        parcel.WeightKg.Should().Be(2.90m);
    }

    [Fact]
    public async Task Weight_Folded_IsIdenticalToRolled()
    {
        var calc = CreateSeeded(out _);

        var rolled = await calc.CalculateAsync(200, 150, 400, 1, PackingMode.Rolled);
        var folded = await calc.CalculateAsync(200, 150, 400, 1, PackingMode.Folded);

        folded.WeightKg.Should().Be(rolled.WeightKg);
    }

    [Fact]
    public async Task Calculate_ZeroWidth_Throws()
    {
        var calc = CreateSeeded(out _);

        var act = () => calc.CalculateAsync(0, 150, 400, 1, PackingMode.Rolled);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Default packing mode (no enum argument) is Rolled
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DefaultOverload_UsesRolledMode()
    {
        var calc = CreateSeeded(out _);

        var defaultParcel = await calc.CalculateAsync(300, 150, 400, 1);
        var rolledParcel  = await calc.CalculateAsync(300, 150, 400, 1, PackingMode.Rolled);

        defaultParcel.LengthCm.Should().Be(rolledParcel.LengthCm);
        defaultParcel.WidthCm.Should().Be(rolledParcel.WidthCm);
        defaultParcel.HeightCm.Should().Be(rolledParcel.HeightCm);
        defaultParcel.WeightKg.Should().Be(rolledParcel.WeightKg);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SplitIntoPackagesAsync (BANNERSH-274): at most MaxItemsPerPackage per package
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Split_ExactMultiple_ProducesCorrectPackageCount()
    {
        // 16 banners → 4 packages of 4
        var calc = CreateSeeded(out _);

        var packages = await calc.SplitIntoPackagesAsync(300, 150, 400, 16, PackingMode.Folded);
        var expectedPkg = await calc.CalculateAsync(300, 150, 400, 4, PackingMode.Folded);

        packages.Should().HaveCount(4);
        packages.Should().AllSatisfy(p => p.WeightKg.Should().Be(expectedPkg.WeightKg));
    }

    [Fact]
    public async Task Split_WithRemainder_LastPackageHasFewer()
    {
        // 14 banners → 3 packages of 4 + 1 package of 2
        var calc = CreateSeeded(out _);

        var packages = await calc.SplitIntoPackagesAsync(300, 150, 400, 14, PackingMode.Folded);

        packages.Should().HaveCount(4);

        var fullPackage = await calc.CalculateAsync(300, 150, 400, 4, PackingMode.Folded);
        var partPackage = await calc.CalculateAsync(300, 150, 400, 2, PackingMode.Folded);

        packages.Take(3).Should().AllSatisfy(p => p.WeightKg.Should().Be(fullPackage.WeightKg));
        packages.Last().WeightKg.Should().Be(partPackage.WeightKg);
    }

    [Fact]
    public async Task Split_SmallQty_ProducesOnePackage()
    {
        // 2 banners → 1 package of 2
        var calc = CreateSeeded(out _);

        var packages = await calc.SplitIntoPackagesAsync(300, 150, 400, 2, PackingMode.Rolled);

        packages.Should().HaveCount(1);
        packages[0].WeightKg.Should().Be(
            (await calc.CalculateAsync(300, 150, 400, 2, PackingMode.Rolled)).WeightKg);
    }

    [Fact]
    public async Task Split_ExactlyMaxItemsPerPackage_IsOnePackage()
    {
        // 4 banners → exactly 1 package of 4
        var calc = CreateSeeded(out _);

        var packages = await calc.SplitIntoPackagesAsync(300, 150, 400, ParcelCalculator.MaxItemsPerPackage, PackingMode.Folded);

        packages.Should().HaveCount(1);
    }

    [Fact]
    public async Task Split_TotalWeight_EqualsPerPackageWeightSummed()
    {
        // Weight of split packages should equal invoking CalculateAsync per group
        var calc = CreateSeeded(out _);
        int qty = 9; // 4 + 4 + 1

        var packages = await calc.SplitIntoPackagesAsync(200, 150, 400, qty, PackingMode.Rolled);

        packages.Should().HaveCount(3);
        var pkg4 = await calc.CalculateAsync(200, 150, 400, 4, PackingMode.Rolled);
        var pkg1 = await calc.CalculateAsync(200, 150, 400, 1, PackingMode.Rolled);
        var expected = pkg4.WeightKg + pkg4.WeightKg + pkg1.WeightKg;
        packages.Sum(p => p.WeightKg).Should().Be(expected);
    }
}
