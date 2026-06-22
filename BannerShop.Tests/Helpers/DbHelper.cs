using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Tests.Helpers;

/// <summary>
/// Factory helpers for creating isolated in-memory DbContext instances per test.
/// </summary>
internal static class DbHelper
{
    /// <summary>Creates a fresh in-memory BannerShopDbContext with a unique database name.</summary>
    public static BannerShopDbContext CreateInMemory(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<BannerShopDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString("N"))
            .Options;
        return new BannerShopDbContext(options);
    }

    /// <summary>Seeds the default set of pricing parameters (matching production seed).</summary>
    public static void SeedPricingParameters(BannerShopDbContext db)
    {
        db.PricingParameters.AddRange(
            new PricingParameter { Id = 1,  Name = "Base price per sqm",           Key = "base_price_per_sqm",             Value = 180m  },
            new PricingParameter { Id = 2,  Name = "Minimum price",                Key = "minimum_price",                  Value = 399m  },
            new PricingParameter { Id = 3,  Name = "Custom width surcharge",       Key = "custom_width_surcharge",         Value = 150m  },
            new PricingParameter { Id = 4,  Name = "Maljepris (per stk)",           Key = "eyelet_price_nok",               Value = 0m    },
            new PricingParameter { Id = 5,  Name = "Express fee",                  Key = "express_fee",                    Value = 500m  },
            new PricingParameter { Id = 6,  Name = "Tube diameter",                Key = "shipping_tube_diameter_cm",      Value = 15m   },
            new PricingParameter { Id = 7,  Name = "Packaging weight",             Key = "shipping_packaging_weight_g",    Value = 500m  },
            new PricingParameter { Id = 8,  Name = "Max length",                   Key = "shipping_max_length_cm",         Value = 240m  },
            new PricingParameter { Id = 9,  Name = "Standard lead time",           Key = "standard_lead_time_days",        Value = 14m   },
            new PricingParameter { Id = 10, Name = "Express lead time",            Key = "express_lead_time_days",         Value = 3m    },
            new PricingParameter { Id = 11, Name = "AI credit pack small price",    Key = "ai_credit_pack_price_nok",             Value = 29m   },
            new PricingParameter { Id = 12, Name = "AI credit pack small count",   Key = "ai_credit_pack_count",                 Value = 5m    },
            new PricingParameter { Id = 13, Name = "AI activation fee",            Key = "ai_banner_activation_fee_nok",         Value = 95m   },
            new PricingParameter { Id = 14, Name = "AI activation credits",        Key = "ai_banner_activation_credits",         Value = 20m   },
            new PricingParameter { Id = 16, Name = "AI credit pack large price",   Key = "ai_credit_pack_large_price_nok",       Value = 95m   },
            new PricingParameter { Id = 17, Name = "AI credit pack large count",   Key = "ai_credit_pack_large_count",           Value = 20m   }
            // PricingParameter id 15 (banner_panel_overlap_cm) was retired by BANNERSH-272.
        );
        db.SaveChanges();
    }

    /// <summary>Seeds materials and banner sizes (matching production seed / BannerShopStartupSeeder).</summary>
    public static void SeedCatalog(BannerShopDbContext db)
    {
        // BANNERSH-259: keep in sync with BannerShopStartupSeeder.
        // mat2 (680g) is available now; mat1 (400g) is future (Aug 2026).
        var mat1 = new Material { Id = 1, Name = "400g innendørs banner",                     WidthCm = 160, WeightGsm = 400, PricePerSqm = 180m, AvailableFrom = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc) };
        var mat2 = new Material { Id = 2, Name = "680g kraftig banner - 3 år utendørs garanti", WidthCm = 180, WeightGsm = 680, PricePerSqm = 140m, AvailableFrom = null };
        db.Materials.AddRange(mat1, mat2);

        // BANNERSH-255 / BANNERSH-259: range-based pricing rules (matches BannerShopStartupSeeder).
        db.BannerSizes.AddRange(
            // Material 2 — 680g outdoor (available now)
            new BannerSize { Id = 1, Name = "680g × 154",  IsActive = true, MaterialId = 2, SortOrder = 10, MinWidthCm = 1,   MaxWidthCm = 700, MinHeightCm = 1,   MaxHeightCm = 154, PricingHeightCm = 154, PricingMultiplier = 1 },
            new BannerSize { Id = 2, Name = "680g × 300",  IsActive = true, MaterialId = 2, SortOrder = 20, MinWidthCm = 1,   MaxWidthCm = 700, MinHeightCm = 154, MaxHeightCm = 300, PricingHeightCm = 154, PricingMultiplier = 2 },
            new BannerSize { Id = 3, Name = "680g × 450",  IsActive = true, MaterialId = 2, SortOrder = 30, MinWidthCm = 1,   MaxWidthCm = 700, MinHeightCm = 300, MaxHeightCm = 450, PricingHeightCm = 154, PricingMultiplier = 3 },
            // Material 1 — 400g indoor (future, available from Aug 2026)
            new BannerSize { Id = 4, Name = "400g × 180",  IsActive = true, MaterialId = 1, SortOrder = 40, MinWidthCm = 1,   MaxWidthCm = 800, MinHeightCm = 1,   MaxHeightCm = 180, PricingHeightCm = 180, PricingMultiplier = 1 },
            new BannerSize { Id = 5, Name = "400g × 355",  IsActive = true, MaterialId = 1, SortOrder = 50, MinWidthCm = 1,   MaxWidthCm = 800, MinHeightCm = 180, MaxHeightCm = 355, PricingHeightCm = 180, PricingMultiplier = 2 },
            new BannerSize { Id = 6, Name = "400g × 530",  IsActive = true, MaterialId = 1, SortOrder = 60, MinWidthCm = 1,   MaxWidthCm = 800, MinHeightCm = 360, MaxHeightCm = 530, PricingHeightCm = 180, PricingMultiplier = 3 },
            // Fixed-price standard sizes
            new BannerSize { Id = 7, Name = "300 × 180 cm — Standard",  IsActive = true, MaterialId = 1, SortOrder = 70, MinWidthCm = 300, MaxWidthCm = 300, MinHeightCm = 180, MaxHeightCm = 180, PricingHeightCm = 180, PricingMultiplier = 1, FixedPrice = 699m  },
            new BannerSize { Id = 8, Name = "267 × 150 cm — Standard",  IsActive = true, MaterialId = 2, SortOrder = 80, MinWidthCm = 266, MaxWidthCm = 268, MinHeightCm = 149, MaxHeightCm = 154, PricingHeightCm = 154, PricingMultiplier = 1, FixedPrice = 849m  }
        );
        db.SaveChanges();
    }

    /// <summary>Creates a test material with sensible defaults.</summary>
    public static Material MakeMaterial(int id = 1, int widthCm = 160, int weightGsm = 400, DateTime? availableFrom = null, decimal pricePerSqm = 180m)
        => new Material
        {
            Id = id,
            Name = $"Test Material {id}",
            WidthCm = widthCm,
            WeightGsm = weightGsm,
            PricePerSqm = pricePerSqm,
            AvailableFrom = availableFrom
        };

    /// <summary>
    /// Creates a range-based pricing rule covering the given (width, height) area
    /// at the supplied pricing height and multiplier. BANNERSH-255.
    /// </summary>
    public static BannerSize MakeSizeRule(
        int id, Material material, int minW, int maxW, int minH, int maxH,
        int pricingHeight, int multiplier = 1, decimal? fixedPrice = null)
        => new BannerSize
        {
            Id = id,
            Name = $"Rule {id}",
            IsActive = true,
            MaterialId = material.Id,
            Material = material,
            SortOrder = id,
            MinWidthCm = minW,
            MaxWidthCm = maxW,
            MinHeightCm = minH,
            MaxHeightCm = maxH,
            PricingHeightCm = pricingHeight,
            PricingMultiplier = multiplier,
            FixedPrice = fixedPrice
        };

    public static User MakeUser(int id = 1, string email = "test@example.com", UserRole role = UserRole.Customer)
        => new User
        {
            Id = id,
            Email = email,
            Name = "Test User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
            Role = role,
            CreatedAt = DateTime.UtcNow
        };
}
