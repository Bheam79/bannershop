using BannerShop.Core.Entities;
using BannerShop.Infrastructure.Data;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests;

public class BannerShopStartupSeederTests
{
    [Fact]
    public async Task SeedCatalogIfEmptyAsync_EmptyDb_SeedsBothMaterials()
    {
        using var db = DbHelper.CreateInMemory();

        await BannerShopStartupSeeder.SeedCatalogIfEmptyAsync(db);

        db.Materials.Should().HaveCount(2);
        var indoor = db.Materials.Single(m => m.Id == 1);
        indoor.Name.Should().Be("400g innendørs banner");
        indoor.WidthCm.Should().Be(160);
        indoor.WeightGsm.Should().Be(400);
        indoor.PricePerSqm.Should().Be(180m);
        indoor.AvailableFrom.Should().Be(new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc));

        var outdoor = db.Materials.Single(m => m.Id == 2);
        outdoor.Name.Should().Be("680g kraftig banner - 3 år utendørs garanti");
        outdoor.WidthCm.Should().Be(180);
        outdoor.WeightGsm.Should().Be(680);
        outdoor.PricePerSqm.Should().Be(140m);
        outdoor.AvailableFrom.Should().BeNull();
    }

    [Fact]
    public async Task SeedCatalogIfEmptyAsync_EmptyDb_SeedsEightBannerSizeRules()
    {
        using var db = DbHelper.CreateInMemory();

        await BannerShopStartupSeeder.SeedCatalogIfEmptyAsync(db);

        db.BannerSizes.Should().HaveCount(8);
        db.BannerSizes.Select(s => s.Id).Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var fixed700 = db.BannerSizes.Single(s => s.Id == 7);
        fixed700.FixedPrice.Should().Be(699m);
        fixed700.MaterialId.Should().Be(1);
        fixed700.MinWidthCm.Should().Be(300);
        fixed700.MaxWidthCm.Should().Be(300);

        var fixed849 = db.BannerSizes.Single(s => s.Id == 8);
        fixed849.FixedPrice.Should().Be(849m);
        fixed849.MaterialId.Should().Be(2);

        var tier1 = db.BannerSizes.Single(s => s.Id == 1);
        tier1.PricingMultiplier.Should().Be(1);
        tier1.FixedPrice.Should().BeNull();

        var tier3 = db.BannerSizes.Single(s => s.Id == 3);
        tier3.PricingMultiplier.Should().Be(3);
        tier3.MinHeightCm.Should().Be(300);
        tier3.MaxHeightCm.Should().Be(450);
    }

    [Fact]
    public async Task SeedCatalogIfEmptyAsync_MaterialsAlreadyPresent_SkipsMaterialSeeding()
    {
        using var db = DbHelper.CreateInMemory();
        db.Materials.Add(DbHelper.MakeMaterial(id: 99, widthCm: 200, weightGsm: 500));
        db.SaveChanges();

        await BannerShopStartupSeeder.SeedCatalogIfEmptyAsync(db);

        db.Materials.Should().HaveCount(1);
        db.Materials.Single().Id.Should().Be(99);
        // BannerSizes table is still empty, so that half seeds independently.
        db.BannerSizes.Should().HaveCount(8);
    }

    [Fact]
    public async Task SeedCatalogIfEmptyAsync_BannerSizesAlreadyPresent_SkipsSizeSeedingButStillSeedsMaterials()
    {
        using var db = DbHelper.CreateInMemory();
        var material = DbHelper.MakeMaterial(id: 1);
        db.Materials.Add(material);
        db.SaveChanges();
        db.BannerSizes.Add(DbHelper.MakeSizeRule(99, material, 1, 100, 1, 100, 100));
        db.SaveChanges();

        // Materials table is non-empty too, so re-run should be a full no-op on both halves.
        await BannerShopStartupSeeder.SeedCatalogIfEmptyAsync(db);

        db.Materials.Should().HaveCount(1);
        db.BannerSizes.Should().HaveCount(1);
        db.BannerSizes.Single().Id.Should().Be(99);
    }

    [Fact]
    public async Task SeedCatalogIfEmptyAsync_CalledTwiceOnEmptyDb_SecondCallIsNoOp()
    {
        using var db = DbHelper.CreateInMemory();

        await BannerShopStartupSeeder.SeedCatalogIfEmptyAsync(db);
        await BannerShopStartupSeeder.SeedCatalogIfEmptyAsync(db);

        db.Materials.Should().HaveCount(2);
        db.BannerSizes.Should().HaveCount(8);
    }
}
