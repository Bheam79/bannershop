using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Infrastructure.Data;

/// <summary>
/// Seed data for <see cref="BannerShopDbContext"/>. Extracted from the context's
/// <c>OnModelCreating</c> so individual seed sets are easy to find and can be
/// reused (e.g. from test setup) without pulling in the full context body.
/// </summary>
public static class BannerShopSeedData
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        SeedMaterials(modelBuilder);
        SeedBannerSizes(modelBuilder);
        SeedPricingParameters(modelBuilder);
        SeedBannerTemplates(modelBuilder);
    }

    private static void SeedMaterials(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Material>().HasData(
            new Material
            {
                Id = 1,
                Name = "400g Frontlit Banner (160cm)",
                WidthCm = 160,
                MaxBannerWidthCm = 160,
                WeightGsm = 400,
                PricePerSqm = 180m,
                AvailableFrom = null
            },
            new Material
            {
                Id = 2,
                Name = "680g Heavy Duty Banner (180cm)",
                WidthCm = 180,
                MaxBannerWidthCm = 180,
                WeightGsm = 680,
                PricePerSqm = 140m,
                AvailableFrom = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }

    private static void SeedBannerSizes(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BannerSize>().HasData(
            new BannerSize { Id = 1, WidthCm = 300, HeightCm = 150, IsCustomWidth = false, Name = "300 × 150 cm", IsActive = true, MaterialId = 1, FixedPrice = null, SortOrder = 1 },
            new BannerSize { Id = 2, WidthCm = 350, HeightCm = 150, IsCustomWidth = false, Name = "350 × 150 cm", IsActive = true, MaterialId = 1, FixedPrice = null, SortOrder = 2 },
            new BannerSize { Id = 3, WidthCm = 400, HeightCm = 150, IsCustomWidth = false, Name = "400 × 150 cm", IsActive = true, MaterialId = 1, FixedPrice = null, SortOrder = 3 },
            new BannerSize { Id = 4, WidthCm = 450, HeightCm = 150, IsCustomWidth = false, Name = "450 × 150 cm", IsActive = true, MaterialId = 1, FixedPrice = null, SortOrder = 4 },
            new BannerSize { Id = 5, WidthCm = 500, HeightCm = 150, IsCustomWidth = false, Name = "500 × 150 cm", IsActive = true, MaterialId = 1, FixedPrice = null, SortOrder = 5 },
            new BannerSize { Id = 6, WidthCm = null, HeightCm = 150, IsCustomWidth = true, Name = "Valgfri bredde × 150 cm", IsActive = true, MaterialId = 1, FixedPrice = null, SortOrder = 6 },
            new BannerSize { Id = 7, WidthCm = 300, HeightCm = 180, IsCustomWidth = false, Name = "300 × 180 cm", IsActive = true, MaterialId = 2, FixedPrice = 699m, SortOrder = 7 },
            new BannerSize { Id = 100, WidthCm = null, HeightCm = 180, IsCustomWidth = true, Name = "Valgfri bredde × 180 cm", IsActive = true, MaterialId = 2, FixedPrice = null, SortOrder = 8 }
        );
    }

    private static void SeedPricingParameters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PricingParameter>().HasData(
            new PricingParameter { Id = 1, Name = "Basispris per kvadratmeter", Key = "base_price_per_sqm", Value = 180m, Description = "NOK per m² for standard banner material (400g)" },
            new PricingParameter { Id = 2, Name = "Minimumspris", Key = "minimum_price", Value = 399m, Description = "Laveste pris for et enkelt banner (NOK)" },
            new PricingParameter { Id = 3, Name = "Tillegg valgfri bredde", Key = "custom_width_surcharge", Value = 150m, Description = "Ekstra kostnad for valgfri bredde (NOK)" },
            // BANNERSH-93: hem is not applicable on PVC banners. Eyelets (maljer) are a
            // per-eyelet addon added to each order item — not baked into the base price.
            new PricingParameter { Id = 4, Name = "Maljepris (per stk)", Key = "eyelet_price_nok", Value = 15m, Description = "Pris per malje (NOK). Tilvalg per banner — ikke inkludert i basispris." },
            new PricingParameter { Id = 5, Name = "Express produksjonstillegg", Key = "express_fee", Value = 500m, Description = "Tillegg for express produksjon (3 dager) i NOK" },
            new PricingParameter { Id = 6, Name = "Forsendelse: rull-diameter (cm)", Key = "shipping_tube_diameter_cm", Value = 15m, Description = "Estimert diameter på rullet banner-tube for forsendelse (cm)" },
            new PricingParameter { Id = 7, Name = "Forsendelse: emballasjevekt (g)", Key = "shipping_packaging_weight_g", Value = 500m, Description = "Vekt av emballasje (tube, lokk, etiketter) i gram" },
            new PricingParameter { Id = 8, Name = "Forsendelse: maks lengde (cm)", Key = "shipping_max_length_cm", Value = 240m, Description = "Maks tube-lengde transportør aksepterer (cm) — Bring Servicepakke" },
            new PricingParameter { Id = 9, Name = "Standard leveringstid (dager)", Key = "standard_lead_time_days", Value = 14m, Description = "Produksjons- og leveringstid for standard ordre (dager fra bestilling)" },
            new PricingParameter { Id = 10, Name = "Express leveringstid (dager)", Key = "express_lead_time_days", Value = 3m, Description = "Produksjonstid for express-ordre (dager fra bestilling, før forsendelse)" },
            // BANNERSH-65: AI credit pool pricing parameters
            // BANNERSH-137: split into small (29 kr / 5 gen) and large (95 kr / 20 gen) tiers.
            new PricingParameter { Id = 11, Name = "AI kreditpakke liten pris (NOK)", Key = "ai_credit_pack_price_nok", Value = 29m, Description = "Pris for liten kreditpakke med AI forslag (NOK)" },
            new PricingParameter { Id = 12, Name = "AI kreditpakke liten antall", Key = "ai_credit_pack_count", Value = 5m, Description = "Antall AI genererings-kreditter per liten kreditpakke" },
            new PricingParameter { Id = 13, Name = "AI aktiveringsgebyr (NOK)", Key = "ai_banner_activation_fee_nok", Value = 95m, Description = "Obligatorisk AI aktiveringsgebyr ved bestilling av banner med AI design (NOK)" },
            new PricingParameter { Id = 14, Name = "AI kreditter ved bestilling", Key = "ai_banner_activation_credits", Value = 20m, Description = "Antall AI kreditter som gis når AI aktiveringsgebyret er betalt" },
            new PricingParameter { Id = 16, Name = "AI kreditpakke stor pris (NOK)", Key = "ai_credit_pack_large_price_nok", Value = 95m, Description = "Pris for stor kreditpakke med AI forslag (NOK)" },
            new PricingParameter { Id = 17, Name = "AI kreditpakke stor antall", Key = "ai_credit_pack_large_count", Value = 20m, Description = "Antall AI genererings-kreditter per stor kreditpakke" },
            // BANNERSH-88: multi-panel pricing — overlap between panels when a banner is wider
            // than Material.MaxBannerWidthCm and must be assembled from multiple panels.
            new PricingParameter { Id = 15, Name = "Panel-overlapp (cm)", Key = "banner_panel_overlap_cm", Value = 5m, Description = "Overlapp i cm mellom panel ved sammenliming av brede banner. Bestemmer pris-multiplikator (×2, ×3, …) når bestilt bredde overstiger materialets maks bredde." }
        );
    }

    private static void SeedBannerTemplates(ModelBuilder modelBuilder)
    {
        // Celebration categories shown in the banner builder.
        modelBuilder.Entity<BannerTemplate>().HasData(
            new BannerTemplate { Id = 1, Category = BannerTemplateCategory.Birthday,     NameNb = "Bursdag",        NameEn = "Birthday",         SortOrder = 10 },
            new BannerTemplate { Id = 8, Category = BannerTemplateCategory.Baptism,      NameNb = "Dåp",            NameEn = "Baptism",          SortOrder = 15 },
            new BannerTemplate { Id = 2, Category = BannerTemplateCategory.Confirmation, NameNb = "Konfirmasjon",   NameEn = "Confirmation",     SortOrder = 20 },
            new BannerTemplate { Id = 3, Category = BannerTemplateCategory.Wedding,      NameNb = "Bryllup",        NameEn = "Wedding",          SortOrder = 30 },
            new BannerTemplate { Id = 4, Category = BannerTemplateCategory.Anniversary,  NameNb = "Jubileum",       NameEn = "Anniversary",      SortOrder = 40 },
            new BannerTemplate { Id = 5, Category = BannerTemplateCategory.Christmas,    NameNb = "Julefeiring",    NameEn = "Christmas party",  SortOrder = 50 },
            new BannerTemplate { Id = 6, Category = BannerTemplateCategory.NewYear,      NameNb = "Nyttårsfeiring", NameEn = "New Year's party", SortOrder = 60 },
            new BannerTemplate { Id = 7, Category = BannerTemplateCategory.Other,        NameNb = "Annen feiring",  NameEn = "Other occasion",   SortOrder = 70 }
        );
    }
}
