using BannerShop.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BannerShop.Infrastructure.Data.Configurations;

public class ShipmentTrackingConfiguration : IEntityTypeConfiguration<ShipmentTracking>
{
    public void Configure(EntityTypeBuilder<ShipmentTracking> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Carrier).HasMaxLength(100).IsRequired();
        e.Property(x => x.TrackingNumber).HasMaxLength(200).IsRequired();
        e.Property(x => x.TrackingUrl).HasMaxLength(500);
        e.HasOne(x => x.Order)
            .WithOne(x => x.ShipmentTracking)
            .HasForeignKey<ShipmentTracking>(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
