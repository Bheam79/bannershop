using BannerShop.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BannerShop.Infrastructure.Data.Configurations;

// Basic banner builder uploads.
public class BannerDesignConfiguration : IEntityTypeConfiguration<BannerDesign>
{
    public void Configure(EntityTypeBuilder<BannerDesign> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        e.Property(x => x.StoragePath).HasMaxLength(500).IsRequired();
        e.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        e.Property(x => x.PreviewStoragePath).HasMaxLength(500);
        e.Property(x => x.IpAddress).HasMaxLength(45);
        // UserId is nullable since BANNERSH-96 (anonymous uploads)
        e.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        // Composite index for user-scoped listing sorted newest-first.
        e.HasIndex(x => new { x.UserId, x.CreatedAt })
         .IsDescending(false, true)
         .HasDatabaseName("IX_BannerDesigns_UserId_CreatedAt");
        e.HasIndex(x => x.IpAddress)
         .HasDatabaseName("IX_BannerDesigns_IpAddress");
    }
}
