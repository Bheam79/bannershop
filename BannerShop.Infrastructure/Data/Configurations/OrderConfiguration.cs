using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BannerShop.Infrastructure.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Status).HasConversion<string>();
        // OrderType and OrderState stored as tinyint for compact storage.
        e.Property(x => x.OrderType)
         .HasConversion<byte>()
         .HasColumnType("tinyint unsigned")
         .HasDefaultValue(OrderType.CustomBanner);
        e.Property(x => x.OrderState)
         .HasConversion<byte>()
         .HasColumnType("tinyint unsigned")
         .HasDefaultValue(OrderState.Draft);
        e.Property(x => x.DeliveryType).HasConversion<string>();
        e.Property(x => x.ShippingCostNok).HasColumnType("decimal(10,2)");
        e.Property(x => x.ExpressFeeNok).HasColumnType("decimal(10,2)");
        e.Property(x => x.AiActivationFeeNok).HasColumnType("decimal(10,2)").HasDefaultValue(0m);
        e.Property(x => x.TotalNok).HasColumnType("decimal(10,2)");
        e.Property(x => x.StripePaymentIntentId).HasMaxLength(200);
        // BANNERSH-185: soft-delete flag for customer-cleared Draft / PendingPayment
        // orders. Indexed so the (UserId, Deleted) and (Deleted) filters used by the
        // customer + admin listings remain fast as the orders table grows.
        e.Property(x => x.Deleted).HasDefaultValue(false);
        e.HasIndex(x => x.Deleted);
        e.HasOne(x => x.User)
            .WithMany(x => x.Orders)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.ShippingAddress)
            .WithMany(x => x.Orders)
            .HasForeignKey(x => x.ShippingAddressId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
