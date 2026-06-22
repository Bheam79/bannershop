using BannerShop.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BannerShop.Infrastructure.Data;

/// <summary>
/// Seeds Materials and BannerSizes on a fresh database (tables must be empty).
/// Unlike EF <c>HasData()</c> this seeder never runs UPDATE or DELETE operations,
/// so admin changes made after initial deployment are never overwritten by a
/// future <c>make up</c> or <c>db.Database.Migrate()</c> call.
/// </summary>
/// <remarks>
/// BANNERSH-257 — replaces the previous <c>HasData()</c> approach that caused
/// migrated UpdateData statements to overwrite production sizes on each redeploy.
/// </remarks>
public static class BannerShopStartupSeeder
{
    public static async Task SeedCatalogIfEmptyAsync(
        BannerShopDbContext db,
        ILogger? logger = null)
    {
        await SeedMaterialsIfEmptyAsync(db, logger);
        await SeedBannerSizesIfEmptyAsync(db, logger);
    }

    // ── Materials ─────────────────────────────────────────────────────────────

    private static async Task SeedMaterialsIfEmptyAsync(BannerShopDbContext db, ILogger? logger)
    {
        if (await db.Materials.AnyAsync())
            return;

        logger?.LogInformation("Seeding default materials (table is empty).");

        // Material 2 (680g) is the current outdoor banner — available now.
        // Material 1 (400g) is an indoor banner not yet in production — available from Aug 2026.
        db.Materials.AddRange(
            new Material
            {
                Id = 1,
                Name = "400g innendørs banner",
                WidthCm = 160,
                MaxBannerWidthCm = 160,
                WeightGsm = 400,
                PricePerSqm = 180m,
                AvailableFrom = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc)
            },
            new Material
            {
                Id = 2,
                Name = "680g kraftig banner - 3 år utendørs garanti",
                WidthCm = 180,
                MaxBannerWidthCm = 180,
                WeightGsm = 680,
                PricePerSqm = 140m,
                AvailableFrom = null
            }
        );
        await db.SaveChangesAsync();
    }

    // ── Banner sizes ──────────────────────────────────────────────────────────

    private static async Task SeedBannerSizesIfEmptyAsync(BannerShopDbContext db, ILogger? logger)
    {
        if (await db.BannerSizes.AnyAsync())
            return;

        logger?.LogInformation("Seeding default banner size rules (table is empty).");

        // Range-based pricing rules (BANNERSH-255).
        //
        // 680g (Material 2, available now): 154 cm height per panel, max width 700 cm.
        // 400g (Material 1, future):        180 cm height per panel, max width 800 cm.
        //
        // Height tiers encode how many panels are needed for a given banner height.
        // Fixed-price rows are for the most common standard sizes.
        db.BannerSizes.AddRange(
            // Material 2 — 680g outdoor (available now)
            new BannerSize { Id = 1, Name = "680g × 154",  IsActive = true, MaterialId = 2, SortOrder = 10, MinWidthCm = 1,   MaxWidthCm = 700, MinHeightCm = 1,   MaxHeightCm = 154, PricingHeightCm = 154, PricingMultiplier = 1, FixedPrice = null },
            new BannerSize { Id = 2, Name = "680g × 300",  IsActive = true, MaterialId = 2, SortOrder = 20, MinWidthCm = 1,   MaxWidthCm = 700, MinHeightCm = 154, MaxHeightCm = 300, PricingHeightCm = 154, PricingMultiplier = 2, FixedPrice = null },
            new BannerSize { Id = 3, Name = "680g × 450",  IsActive = true, MaterialId = 2, SortOrder = 30, MinWidthCm = 1,   MaxWidthCm = 700, MinHeightCm = 300, MaxHeightCm = 450, PricingHeightCm = 154, PricingMultiplier = 3, FixedPrice = null },

            // Material 1 — 400g indoor (future, available from Aug 2026)
            new BannerSize { Id = 4, Name = "400g × 180",  IsActive = true, MaterialId = 1, SortOrder = 40, MinWidthCm = 1,   MaxWidthCm = 800, MinHeightCm = 1,   MaxHeightCm = 180, PricingHeightCm = 180, PricingMultiplier = 1, FixedPrice = null },
            new BannerSize { Id = 5, Name = "400g × 355",  IsActive = true, MaterialId = 1, SortOrder = 50, MinWidthCm = 1,   MaxWidthCm = 800, MinHeightCm = 180, MaxHeightCm = 355, PricingHeightCm = 180, PricingMultiplier = 2, FixedPrice = null },
            new BannerSize { Id = 6, Name = "400g × 530",  IsActive = true, MaterialId = 1, SortOrder = 60, MinWidthCm = 1,   MaxWidthCm = 800, MinHeightCm = 360, MaxHeightCm = 530, PricingHeightCm = 180, PricingMultiplier = 3, FixedPrice = null },

            // Fixed-price standard sizes
            new BannerSize { Id = 7, Name = "300 × 180 cm — Standard",  IsActive = true, MaterialId = 1, SortOrder = 70, MinWidthCm = 300, MaxWidthCm = 300, MinHeightCm = 180, MaxHeightCm = 180, PricingHeightCm = 180, PricingMultiplier = 1, FixedPrice = 699m  },
            new BannerSize { Id = 8, Name = "267 × 150 cm -- Standard", IsActive = true, MaterialId = 2, SortOrder = 80, MinWidthCm = 266, MaxWidthCm = 268, MinHeightCm = 149, MaxHeightCm = 154, PricingHeightCm = 154, PricingMultiplier = 1, FixedPrice = 849m  }
        );
        await db.SaveChangesAsync();
    }
}
