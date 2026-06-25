using BannerShop.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BannerShop.Infrastructure.Data.Configurations;

public class PageViewConfiguration : IEntityTypeConfiguration<PageView>
{
    public void Configure(EntityTypeBuilder<PageView> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.SessionId).HasMaxLength(36).IsRequired();
        e.Property(x => x.Path).HasMaxLength(500).IsRequired();
        e.Property(x => x.Referrer).HasMaxLength(1000);
        e.Property(x => x.ReferrerSource).HasMaxLength(20).IsRequired();
        e.Property(x => x.IpAddress).HasMaxLength(45);

        // Main query pattern: time-range + grouping
        e.HasIndex(x => x.CreatedAt).HasDatabaseName("IX_PageViews_CreatedAt");
        // Session look-up (IsNewSession check, session aggregation)
        e.HasIndex(x => x.SessionId).HasDatabaseName("IX_PageViews_SessionId");
        // Referrer source filter
        e.HasIndex(x => x.ReferrerSource).HasDatabaseName("IX_PageViews_ReferrerSource");
    }
}
