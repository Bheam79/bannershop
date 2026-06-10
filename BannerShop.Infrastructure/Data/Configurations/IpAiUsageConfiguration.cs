using BannerShop.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BannerShop.Infrastructure.Data.Configurations;

// BANNERSH-65: anonymous generation throttling by IP
public class IpAiUsageConfiguration : IEntityTypeConfiguration<IpAiUsage>
{
    public void Configure(EntityTypeBuilder<IpAiUsage> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.IpAddress).HasMaxLength(45).IsRequired();
        e.HasIndex(x => x.IpAddress).HasDatabaseName("IX_IpAiUsages_IpAddress");
    }
}
