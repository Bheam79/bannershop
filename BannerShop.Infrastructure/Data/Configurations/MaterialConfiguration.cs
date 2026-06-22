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
    }
}
