using BannerShop.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BannerShop.Infrastructure.Data.Configurations;

public class PricingParameterConfiguration : IEntityTypeConfiguration<PricingParameter>
{
    public void Configure(EntityTypeBuilder<PricingParameter> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Key).HasMaxLength(100).IsRequired();
        e.HasIndex(x => x.Key).IsUnique();
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.Property(x => x.Value).HasColumnType("decimal(10,2)");
    }
}
