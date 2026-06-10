using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BannerShop.Infrastructure.Data.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.AreaSqm).HasColumnType("decimal(10,4)");
        e.Property(x => x.UnitPriceNok).HasColumnType("decimal(10,2)");
        e.Property(x => x.LineTotalNok).HasColumnType("decimal(10,2)");
        // BANNERSH-93: eyelet (malje) addon — stored as string in DB for readability.
        e.Property(x => x.EyeletOption).HasConversion<string>().HasMaxLength(20).HasDefaultValue(EyeletOption.None);
        e.Property(x => x.EyeletCount).HasDefaultValue(0);
        e.Property(x => x.EyeletFeeNok).HasColumnType("decimal(10,2)").HasDefaultValue(0m);
        e.HasOne(x => x.Order)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.BannerSize)
            .WithMany(x => x.OrderItems)
            .HasForeignKey(x => x.BannerSizeId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasOne(x => x.BannerDesign)
            .WithMany()
            .HasForeignKey(x => x.BannerDesignId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.DesignRequest)
            .WithMany()
            .HasForeignKey(x => x.DesignRequestId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
