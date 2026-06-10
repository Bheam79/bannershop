using BannerShop.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BannerShop.Infrastructure.Data.Configurations;

// Correction log per design request.
public class DesignRequestRevisionConfiguration : IEntityTypeConfiguration<DesignRequestRevision>
{
    public void Configure(EntityTypeBuilder<DesignRequestRevision> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.CustomerComment).HasMaxLength(2000).IsRequired();
        e.HasOne(x => x.DesignRequest)
            .WithMany(x => x.Revisions)
            .HasForeignKey(x => x.DesignRequestId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.DesignRequestId);
    }
}
