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
            new SystemSetting { Id = 2, Key = "openai_image_model", Value = "", Label = "OpenAI Image Model (blank = use config default)", IsSensitive = false },
            // BANNERSH-127: ImageQuality wasn't editable via the admin panel — operators had
            // to redeploy appsettings.Local.json to change it. Add a DB-backed row so it can
            // be flipped between low/medium/high/auto at runtime, with appsettings as the
            // fallback when this row is blank.
            new SystemSetting { Id = 3, Key = "openai_image_quality", Value = "", Label = "OpenAI Image Quality (blank = use config default; allowed: low, medium, high, auto)", IsSensitive = false },
            // BANNERSH-160 / BANNERSH-161: Stripe keys are now DB-only (no appsettings fallback).
            // The admin enters them via the settings panel; on first boot the rows are seeded
            // empty and payment endpoints return a configured-error until they are set.
            new SystemSetting { Id = 4, Key = "stripe_secret_key",      Value = "", Label = "Stripe Secret Key (sk_live_… / sk_test_… / rk_live_… / rk_test_…)", IsSensitive = true },
            new SystemSetting { Id = 5, Key = "stripe_publishable_key", Value = "", Label = "Stripe Publishable Key (pk_live_… / pk_test_…)",                  IsSensitive = false },
            new SystemSetting { Id = 6, Key = "stripe_webhook_secret",  Value = "", Label = "Stripe Webhook Secret (whsec_…)",                                  IsSensitive = true }
        );
    }
}
