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
            new PricingParameter { Id = 1,  Name = "Base price per sqm",       Key = "base_price_per_sqm",         Value = 180m  },
            new PricingParameter { Id = 2,  Name = "Minimum price",             Key = "minimum_price",              Value = 399m  },
            new PricingParameter { Id = 3,  Name = "Custom width surcharge",    Key = "custom_width_surcharge",     Value = 150m  },
            new PricingParameter { Id = 4,  Name = "Hem flat fee",              Key = "hem_and_eyelets_flat_fee",   Value = 0m    },
            new PricingParameter { Id = 5,  Name = "Express fee",               Key = "express_fee",                Value = 500m  },
            new PricingParameter { Id = 6,  Name = "Tube diameter",             Key = "shipping_tube_diameter_cm",  Value = 15m   },
            new PricingParameter { Id = 7,  Name = "Packaging weight",          Key = "shipping_packaging_weight_g",Value = 500m  },
            new PricingParameter { Id = 8,  Name = "Max length",                Key = "shipping_max_length_cm",     Value = 240m  },
            new PricingParameter { Id = 9,  Name = "Standard lead time",        Key = "standard_lead_time_days",    Value = 14m   },
            new PricingParameter { Id = 10, Name = "Express lead time",         Key = "express_lead_time_days",     Value = 3m    }
        );
        db.SaveChanges();
    }

    /// <summary>Seeds materials and banner sizes (matching production seed).</summary>
    public static void SeedCatalog(BannerShopDbContext db)
    {
        var mat1 = new Material { Id = 1, Name = "400g Frontlit Banner (160cm)", WidthCm = 160, WeightGsm = 400, PricePerSqm = 180m, AvailableFrom = null };
        var mat2 = new Material { Id = 2, Name = "680g Heavy Duty Banner (180cm)", WidthCm = 180, WeightGsm = 680, PricePerSqm = 140m, AvailableFrom = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc) };
        db.Materials.AddRange(mat1, mat2);

        db.BannerSizes.AddRange(
            new BannerSize { Id = 1, WidthCm = 300, HeightCm = 150, IsCustomWidth = false, Name = "300 × 150 cm", IsActive = true, MaterialId = 1, SortOrder = 1 },
            new BannerSize { Id = 2, WidthCm = 350, HeightCm = 150, IsCustomWidth = false, Name = "350 × 150 cm", IsActive = true, MaterialId = 1, SortOrder = 2 },
            new BannerSize { Id = 6, WidthCm = null, HeightCm = 150, IsCustomWidth = true,  Name = "Custom × 150 cm", IsActive = true, MaterialId = 1, SortOrder = 6 },
            new BannerSize { Id = 7, WidthCm = 300, HeightCm = 180, IsCustomWidth = false, Name = "300 × 180 cm", IsActive = true, MaterialId = 2, FixedPrice = 699m, SortOrder = 7 }
        );
        db.SaveChanges();
    }

    /// <summary>Creates a test material with sensible defaults.</summary>
    public static Material MakeMaterial(int id = 1, int widthCm = 160, int weightGsm = 400, DateTime? availableFrom = null)
        => new Material
        {
            Id = id,
            Name = $"Test Material {id}",
            WidthCm = widthCm,
            WeightGsm = weightGsm,
            PricePerSqm = 180m,
            AvailableFrom = availableFrom
        };

    /// <summary>Creates a standard (fixed-width) banner size with the given material attached.</summary>
    public static BannerSize MakeStandardSize(int id, int widthCm, int heightCm, Material material, decimal? fixedPrice = null)
        => new BannerSize
        {
            Id = id,
            WidthCm = widthCm,
            HeightCm = heightCm,
            IsCustomWidth = false,
            Name = $"{widthCm} × {heightCm} cm",
            IsActive = true,
            MaterialId = material.Id,
            Material = material,
            FixedPrice = fixedPrice
        };

    /// <summary>Creates a custom-width banner size with the given material attached.</summary>
    public static BannerSize MakeCustomWidthSize(int id, int heightCm, Material material)
        => new BannerSize
        {
            Id = id,
            WidthCm = null,
            HeightCm = heightCm,
            IsCustomWidth = true,
            Name = $"Custom × {heightCm} cm",
            IsActive = true,
            MaterialId = material.Id,
            Material = material
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
