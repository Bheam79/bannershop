using BannerShop.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BannerShop.Infrastructure.Data.Configurations;

public class BannerSizeConfiguration : IEntityTypeConfiguration<BannerSize>
{
    public void Configure(EntityTypeBuilder<BannerSize> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        e.Property(x => x.FixedPrice).HasColumnType("decimal(10,2)");
        e.HasOne(x => x.Material)
            .WithMany(x => x.BannerSizes)
            .HasForeignKey(x => x.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
