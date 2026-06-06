using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Infrastructure.Data;

public class BannerShopDbContext : DbContext
{
    public BannerShopDbContext(DbContextOptions<BannerShopDbContext> options) : base(options) { }

    public DbSet<Material> Materials => Set<Material>();
    public DbSet<BannerSize> BannerSizes => Set<BannerSize>();
    public DbSet<PricingParameter> PricingParameters => Set<PricingParameter>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<ProductionStatus> ProductionStatuses => Set<ProductionStatus>();
    public DbSet<ShipmentTracking> ShipmentTrackings => Set<ShipmentTracking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Material
        modelBuilder.Entity<Material>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.PricePerSqm).HasColumnType("decimal(10,2)");
        });

        // BannerSize
        modelBuilder.Entity<BannerSize>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.FixedPrice).HasColumnType("decimal(10,2)");
            e.HasOne(x => x.Material)
                .WithMany(x => x.BannerSizes)
                .HasForeignKey(x => x.MaterialId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // PricingParameter
        modelBuilder.Entity<PricingParameter>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Value).HasColumnType("decimal(10,2)");
        });

        // User
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(255).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.Role).HasConversion<string>();
        });

        // RefreshToken
        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Token).HasMaxLength(500).IsRequired();
            e.HasOne(x => x.User)
                .WithMany(x => x.RefreshTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Address
        modelBuilder.Entity<Address>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Line1).HasMaxLength(200).IsRequired();
            e.Property(x => x.Line2).HasMaxLength(200);
            e.Property(x => x.PostalCode).HasMaxLength(20).IsRequired();
            e.Property(x => x.City).HasMaxLength(100).IsRequired();
            e.Property(x => x.Country).HasMaxLength(10).HasDefaultValue("NO");
            e.HasOne(x => x.User)
                .WithMany(x => x.Addresses)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Order
        modelBuilder.Entity<Order>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.DeliveryType).HasConversion<string>();
            e.Property(x => x.ShippingCostNok).HasColumnType("decimal(10,2)");
            e.Property(x => x.ExpressFeeNok).HasColumnType("decimal(10,2)");
            e.Property(x => x.TotalNok).HasColumnType("decimal(10,2)");
            e.Property(x => x.StripePaymentIntentId).HasMaxLength(200);
            e.HasOne(x => x.User)
                .WithMany(x => x.Orders)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ShippingAddress)
                .WithMany(x => x.Orders)
                .HasForeignKey(x => x.ShippingAddressId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // OrderItem
        modelBuilder.Entity<OrderItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.AreaSqm).HasColumnType("decimal(10,4)");
            e.Property(x => x.UnitPriceNok).HasColumnType("decimal(10,2)");
            e.Property(x => x.LineTotalNok).HasColumnType("decimal(10,2)");
            e.HasOne(x => x.Order)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.BannerSize)
                .WithMany(x => x.OrderItems)
                .HasForeignKey(x => x.BannerSizeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ProductionStatus
        modelBuilder.Entity<ProductionStatus>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Stage).HasConversion<string>();
            e.HasOne(x => x.OrderItem)
                .WithMany(x => x.ProductionStatuses)
                .HasForeignKey(x => x.OrderItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ShipmentTracking
        modelBuilder.Entity<ShipmentTracking>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Carrier).HasMaxLength(100).IsRequired();
            e.Property(x => x.TrackingNumber).HasMaxLength(200).IsRequired();
            e.Property(x => x.TrackingUrl).HasMaxLength(500);
            e.HasOne(x => x.Order)
                .WithOne(x => x.ShipmentTracking)
                .HasForeignKey<ShipmentTracking>(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Seed data
        SeedData(modelBuilder);
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        // Seed Materials
        modelBuilder.Entity<Material>().HasData(
            new Material
            {
                Id = 1,
                Name = "400g Frontlit Banner (160cm)",
                WidthCm = 160,
                WeightGsm = 400,
                PricePerSqm = 180m,
                AvailableFrom = null
            },
            new Material
            {
                Id = 2,
                Name = "680g Heavy Duty Banner (180cm)",
                WidthCm = 180,
                WeightGsm = 680,
                PricePerSqm = 140m,
                AvailableFrom = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc)
            }
        );

        // Seed BannerSizes
        modelBuilder.Entity<BannerSize>().HasData(
            new BannerSize { Id = 1, WidthCm = 300, HeightCm = 150, IsCustomWidth = false, Name = "300 × 150 cm", IsActive = true, MaterialId = 1, FixedPrice = null, SortOrder = 1 },
            new BannerSize { Id = 2, WidthCm = 350, HeightCm = 150, IsCustomWidth = false, Name = "350 × 150 cm", IsActive = true, MaterialId = 1, FixedPrice = null, SortOrder = 2 },
            new BannerSize { Id = 3, WidthCm = 400, HeightCm = 150, IsCustomWidth = false, Name = "400 × 150 cm", IsActive = true, MaterialId = 1, FixedPrice = null, SortOrder = 3 },
            new BannerSize { Id = 4, WidthCm = 450, HeightCm = 150, IsCustomWidth = false, Name = "450 × 150 cm", IsActive = true, MaterialId = 1, FixedPrice = null, SortOrder = 4 },
            new BannerSize { Id = 5, WidthCm = 500, HeightCm = 150, IsCustomWidth = false, Name = "500 × 150 cm", IsActive = true, MaterialId = 1, FixedPrice = null, SortOrder = 5 },
            new BannerSize { Id = 6, WidthCm = null, HeightCm = 150, IsCustomWidth = true, Name = "Valgfri bredde × 150 cm", IsActive = true, MaterialId = 1, FixedPrice = null, SortOrder = 6 },
            new BannerSize { Id = 7, WidthCm = 300, HeightCm = 180, IsCustomWidth = false, Name = "300 × 180 cm", IsActive = true, MaterialId = 2, FixedPrice = 699m, SortOrder = 7 }
        );

        // Seed PricingParameters
        modelBuilder.Entity<PricingParameter>().HasData(
            new PricingParameter { Id = 1, Name = "Basispris per kvadratmeter", Key = "base_price_per_sqm", Value = 180m, Description = "NOK per m² for standard banner material (400g)" },
            new PricingParameter { Id = 2, Name = "Minimumspris", Key = "minimum_price", Value = 399m, Description = "Laveste pris for et enkelt banner (NOK)" },
            new PricingParameter { Id = 3, Name = "Tillegg valgfri bredde", Key = "custom_width_surcharge", Value = 150m, Description = "Ekstra kostnad for valgfri bredde (NOK)" },
            new PricingParameter { Id = 4, Name = "Hem og øyebolter (flat avgift)", Key = "hem_and_eyelets_flat_fee", Value = 0m, Description = "Fast avgift for hem og øyebolter - inkludert i basispris" },
            new PricingParameter { Id = 5, Name = "Express produksjonstillegg", Key = "express_fee", Value = 500m, Description = "Tillegg for express produksjon (3 dager) i NOK" },
            new PricingParameter { Id = 6, Name = "Forsendelse: rull-diameter (cm)", Key = "shipping_tube_diameter_cm", Value = 15m, Description = "Estimert diameter på rullet banner-tube for forsendelse (cm)" },
            new PricingParameter { Id = 7, Name = "Forsendelse: emballasjevekt (g)", Key = "shipping_packaging_weight_g", Value = 500m, Description = "Vekt av emballasje (tube, lokk, etiketter) i gram" },
            new PricingParameter { Id = 8, Name = "Forsendelse: maks lengde (cm)", Key = "shipping_max_length_cm", Value = 240m, Description = "Maks tube-lengde transportør aksepterer (cm) — Bring Servicepakke" }
        );
    }
}
