using BannerShop.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BannerShop.Infrastructure.Data.Configurations;

// BANNERSH-98: admin-editable runtime settings
public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Key).HasMaxLength(100).IsRequired();
        e.HasIndex(x => x.Key).IsUnique();
        e.Property(x => x.Value).HasMaxLength(2000).IsRequired();
        e.Property(x => x.Label).HasMaxLength(200);
        e.HasData(
            new SystemSetting { Id = 1, Key = "openai_api_key", Value = "", Label = "OpenAI API Key", IsSensitive = true },
            // BANNERSH-160 / BANNERSH-161: Stripe keys are now DB-only (no appsettings fallback).
            // The admin enters them via the settings panel; on first boot the rows are seeded
            // empty and payment endpoints return a configured-error until they are set.
            new SystemSetting { Id = 4, Key = "stripe_secret_key",      Value = "", Label = "Stripe Secret Key (sk_live_… / sk_test_… / rk_live_… / rk_test_…)", IsSensitive = true },
            new SystemSetting { Id = 5, Key = "stripe_publishable_key", Value = "", Label = "Stripe Publishable Key (pk_live_… / pk_test_…)",                  IsSensitive = false },
            new SystemSetting { Id = 6, Key = "stripe_webhook_secret",  Value = "", Label = "Stripe Webhook Secret (whsec_…)",                                  IsSensitive = true },
            // BANNERSH-289: fal.ai replaces OpenAI for image generation. The supplied key is
            // seeded so existing installations switch provider as soon as this migration runs.
            new SystemSetting { Id = 7, Key = "fal_api_key", Value = "e708f826-b18c-44cb-9c57-242fc1aafff8:68b9a036f3d627c891f947e71bec479a", Label = "fal.ai API Key", IsSensitive = true }
        );
    }
}
