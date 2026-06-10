using BannerShop.Core.Entities;
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
    public DbSet<BannerDesign> BannerDesigns => Set<BannerDesign>();
    public DbSet<BannerTemplate> BannerTemplates => Set<BannerTemplate>();
    public DbSet<DesignRequest> DesignRequests => Set<DesignRequest>();
    public DbSet<DesignRequestRevision> DesignRequestRevisions => Set<DesignRequestRevision>();
    public DbSet<BannerGeneration> BannerGenerations => Set<BannerGeneration>();
    public DbSet<IpAiUsage> IpAiUsages => Set<IpAiUsage>();
    public DbSet<AiCreditTransaction> AiCreditTransactions => Set<AiCreditTransaction>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Per-entity configuration lives in BannerShop.Infrastructure.Data.Configurations.
        // BANNERSH-200 split the previous monolithic OnModelCreating into one
        // IEntityTypeConfiguration<T> per entity for findability + smaller diffs.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BannerShopDbContext).Assembly);

        // Seed data lives in BannerShopSeedData so it can be applied here AND from
        // test setup without dragging in the rest of the context body.
        BannerShopSeedData.Apply(modelBuilder);
    }
}
