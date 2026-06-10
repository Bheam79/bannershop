using BannerShop.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BannerShop.Infrastructure.Data.Configurations;

// AI / Manual design jobs — own mini-order with its own PaymentIntent.
public class DesignRequestConfiguration : IEntityTypeConfiguration<DesignRequest>
{
    public void Configure(EntityTypeBuilder<DesignRequest> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20).IsRequired();
        e.Property(x => x.Language).HasMaxLength(5).IsRequired().HasDefaultValue("nb");
        e.Property(x => x.PersonName).HasMaxLength(200).IsRequired();
        e.Property(x => x.TextContent).HasMaxLength(1000).IsRequired();
        e.Property(x => x.ThemeDescription).HasMaxLength(1000).IsRequired();
        e.Property(x => x.UploadedPhotoPath).HasMaxLength(500);
        e.Property(x => x.AspectRatio).HasMaxLength(10).IsRequired().HasDefaultValue("16:9");
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        e.Property(x => x.PriceNok).HasColumnType("decimal(10,2)");
        // BANNERSH-104: snapshot of the physical-banner production cost, charged
        // alongside the design fee on the Manual flow. Defaults to 0 on existing
        // rows (legacy + AI requests, whose production cost is handled elsewhere).
        e.Property(x => x.BannerPriceNok).HasColumnType("decimal(10,2)").HasDefaultValue(0m);
        e.Property(x => x.StripePaymentIntentId).HasMaxLength(200);
        e.Property(x => x.AiResultStoragePath).HasMaxLength(500);
        e.Property(x => x.DesignerPreviewPath).HasMaxLength(500);
        e.Property(x => x.FinalCroppedStoragePath).HasMaxLength(500);
        e.Property(x => x.LastError).HasMaxLength(2000);
        e.Property(x => x.DesignerNotes).HasMaxLength(2000);
        e.Property(x => x.IpAddress).HasMaxLength(45);

        // UserId is nullable (BANNERSH-67: free-first anonymous flow).
        e.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        e.HasOne(x => x.BannerTemplate)
            .WithMany()
            .HasForeignKey(x => x.BannerTemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        e.HasOne(x => x.FinalBannerDesign)
            .WithMany()
            .HasForeignKey(x => x.FinalBannerDesignId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional FK to the Order that contains this design request (BANNERSH-107).
        // Nullable — filled in by a follow-up linking migration; existing rows remain null.
        e.HasOne(x => x.Order)
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        e.HasIndex(x => x.UserId);
        e.HasIndex(x => x.StripePaymentIntentId);
        e.HasIndex(x => x.Status);
        e.HasIndex(x => x.IpAddress).HasDatabaseName("IX_DesignRequests_IpAddress");
        e.HasIndex(x => x.OrderId).HasDatabaseName("IX_DesignRequests_OrderId");

        // CurrentGenerationId: explicit FK to BannerGenerations (no cascade to avoid cycles).
        e.HasOne(x => x.CurrentGeneration)
            .WithMany()
            .HasForeignKey(x => x.CurrentGenerationId)
            .OnDelete(DeleteBehavior.SetNull);
        e.Property(x => x.CurrentGenerationId);
    }
}
