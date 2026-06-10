using BannerShop.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BannerShop.Infrastructure.Data.Configurations;

// Pre-defined celebration categories.
public class BannerTemplateConfiguration : IEntityTypeConfiguration<BannerTemplate>
{
    public void Configure(EntityTypeBuilder<BannerTemplate> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Category).HasConversion<string>().HasMaxLength(50).IsRequired();
        e.Property(x => x.NameNb).HasMaxLength(100).IsRequired();
        e.Property(x => x.NameEn).HasMaxLength(100).IsRequired();
        e.HasIndex(x => x.SortOrder);
    }
}
