using BannerShop.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BannerShop.Infrastructure.Data.Configurations;

// BANNERSH-66: one row per AI pipeline attempt.
public class BannerGenerationConfiguration : IEntityTypeConfiguration<BannerGeneration>
{
    public void Configure(EntityTypeBuilder<BannerGeneration> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.StoragePath).HasMaxLength(500);
        e.Property(x => x.CroppedStoragePath).HasMaxLength(500);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        e.Property(x => x.ErrorMessage).HasMaxLength(2000);
        e.HasOne(x => x.DesignRequest)
            .WithMany(x => x.Generations)
            .HasForeignKey(x => x.DesignRequestId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.DesignRequestId).HasDatabaseName("IX_BannerGenerations_DesignRequestId");
    }
}
