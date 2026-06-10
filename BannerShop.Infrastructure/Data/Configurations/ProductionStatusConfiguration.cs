using BannerShop.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BannerShop.Infrastructure.Data.Configurations;

public class ProductionStatusConfiguration : IEntityTypeConfiguration<ProductionStatus>
{
    public void Configure(EntityTypeBuilder<ProductionStatus> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Stage).HasConversion<string>();
        e.HasOne(x => x.OrderItem)
            .WithMany(x => x.ProductionStatuses)
            .HasForeignKey(x => x.OrderItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
