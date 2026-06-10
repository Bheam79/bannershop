using BannerShop.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BannerShop.Infrastructure.Data.Configurations;

public class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        e.Property(x => x.PricePerSqm).HasColumnType("decimal(10,2)");
        // Max banner width without gluing multiple panels together (BANNERSH-88).
        // Defaults to 0 at the SQL level — the migration backfills existing rows with
        // their roll width, and PricingService treats a non-positive value as "fall back
        // to Material.WidthCm" so manually-inserted rows still work.
        e.Property(x => x.MaxBannerWidthCm).HasDefaultValue(0);
    }
}
