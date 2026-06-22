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
            new PricingParameter { Id = 15, Name = "Banner panel overlap",         Key = "banner_panel_overlap_cm",              Value = 5m    },
            new PricingParameter { Id = 16, Name = "AI credit pack large price",   Key = "ai_credit_pack_large_price_nok",       Value = 95m   },
            new PricingParameter { Id = 17, Name = "AI credit pack large count",   Key = "ai_credit_pack_large_count",           Value = 20m   }
        );
        db.SaveChanges();
    }

    /// <summary>Seeds materials and banner sizes (matching production seed).</summary>
    public static void SeedCatalog(BannerShopDbContext db)
    {
        var mat1 = new Material { Id = 1, Name = "400g Frontlit Banner (160cm)", WidthCm = 160, MaxBannerWidthCm = 160, WeightGsm = 400, PricePerSqm = 180m, AvailableFrom = null };
        var mat2 = new Material { Id = 2, Name = "680g Heavy Duty Banner (180cm)", WidthCm = 180, MaxBannerWidthCm = 180, WeightGsm = 680, PricePerSqm = 140m, AvailableFrom = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc) };
        db.Materials.AddRange(mat1, mat2);

        // BANNERSH-255: range-based pricing rules (matches BannerShopSeedData.SeedBannerSizes).
        db.BannerSizes.AddRange(
            new BannerSize { Id = 1, Name = "400g 1p (h 1–154)",  IsActive = true, MaterialId = 1, SortOrder = 10, MinWidthCm = 1, MaxWidthCm = 500, MinHeightCm = 1,   MaxHeightCm = 154, PricingHeightCm = 154, PricingMultiplier = 1 },
            new BannerSize { Id = 2, Name = "400g 2p (h 154–300)", IsActive = true, MaterialId = 1, SortOrder = 20, MinWidthCm = 1, MaxWidthCm = 500, MinHeightCm = 154, MaxHeightCm = 300, PricingHeightCm = 154, PricingMultiplier = 2 },
            new BannerSize { Id = 6, Name = "680g 1p (h 1–180)",   IsActive = true, MaterialId = 2, SortOrder = 60, MinWidthCm = 1, MaxWidthCm = 500, MinHeightCm = 1,   MaxHeightCm = 180, PricingHeightCm = 180, PricingMultiplier = 1 },
            new BannerSize { Id = 7, Name = "300 × 180 (fast)",    IsActive = true, MaterialId = 2, SortOrder = 70, MinWidthCm = 300, MaxWidthCm = 300, MinHeightCm = 180, MaxHeightCm = 180, PricingHeightCm = 180, PricingMultiplier = 1, FixedPrice = 699m }
        );
        db.SaveChanges();
    }

    /// <summary>Creates a test material with sensible defaults.</summary>
    public static Material MakeMaterial(int id = 1, int widthCm = 160, int weightGsm = 400, DateTime? availableFrom = null, int? maxBannerWidthCm = null, decimal pricePerSqm = 180m)
        => new Material
        {
            Id = id,
            Name = $"Test Material {id}",
            WidthCm = widthCm,
            MaxBannerWidthCm = maxBannerWidthCm ?? 10_000,
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
