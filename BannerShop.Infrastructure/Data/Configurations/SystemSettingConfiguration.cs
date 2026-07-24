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
        // Prompt templates are admin-editable and may be substantially longer
        // than API keys. 8K keeps the row bounded without truncating art direction.
        e.Property(x => x.Value).HasMaxLength(8000).IsRequired();
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
            new SystemSetting { Id = 7, Key = "fal_api_key", Value = "e708f826-b18c-44cb-9c57-242fc1aafff8:68b9a036f3d627c891f947e71bec479a", Label = "fal.ai API Key", IsSensitive = true },
            new SystemSetting { Id = 8, Key = "claude_code_oauth_token", Value = "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", Label = "Claude Code long-lived OAuth token", IsSensitive = true },
            new SystemSetting { Id = 9, Key = "claude_flux_system_prompt", Value = "You are an expert advertising art director and prompt engineer. Turn the supplied customer details into one vivid, highly specific English image-generation prompt for FLUX.2 Pro. The output image IS the finished large-format print banner: never show a banner, sign, poster, print, frame, mockup, wall, room, hanging fabric, or banner-within-a-banner. Demand a premium designed graphic composition rather than a plain photo collage. Explicitly describe the background scene, rich colour palette, lighting, layered decorative framing, depth, energy, subject placement, and large legible typography whose colour, shading and effects suit the scene. When @image1 is available, place that exact person as a professionally retouched integrated cutout, preserve their recognizable identity, and describe a tasteful themed outfit transformation. Keep all important faces and text at least 10% inside every edge. Preserve every supplied text string exactly and request no extra words. Convert trademarked characters or brands into descriptive, original visual attributes without names or logos. Specify sharp, photorealistic, print-quality detail and the requested aspect ratio. Reply with the final FLUX prompt only: one paragraph, no preamble, markdown or quotation marks around the whole answer.", Label = "Claude → FLUX main prompt", IsSensitive = false },
            new SystemSetting { Id = 10, Key = "claude_flux_category_birthday", Value = "Create a celebratory birthday banner with vivid, joyful imagery, playful party energy, premium decorations and an age-appropriate visual style.", Label = "Claude category prompt — Birthday", IsSensitive = false },
            new SystemSetting { Id = 11, Key = "claude_flux_category_confirmation", Value = "Create an elegant Norwegian confirmation banner with dignified modern styling, refined celebratory details and a confident youthful atmosphere.", Label = "Claude category prompt — Confirmation", IsSensitive = false },
            new SystemSetting { Id = 12, Key = "claude_flux_category_wedding", Value = "Create a formal, romantic and elegant wedding banner with luxurious floral or decorative styling and a timeless premium finish.", Label = "Claude category prompt — Wedding", IsSensitive = false },
            new SystemSetting { Id = 13, Key = "claude_flux_category_anniversary", Value = "Create a warm, sophisticated anniversary banner celebrating shared history with elegant layered details and timeless visual richness.", Label = "Claude category prompt — Anniversary", IsSensitive = false },
            new SystemSetting { Id = 14, Key = "claude_flux_category_christmas", Value = "Create a vivid premium Christmas banner with atmospheric seasonal light, rich festive depth and elegant holiday decorations.", Label = "Claude category prompt — Christmas", IsSensitive = false },
            new SystemSetting { Id = 15, Key = "claude_flux_category_new_year", Value = "Create a glamorous, energetic New Year banner with dramatic celebration lighting, sparkling depth and a premium midnight-party atmosphere.", Label = "Claude category prompt — New Year", IsSensitive = false },
            new SystemSetting { Id = 16, Key = "claude_flux_category_other", Value = "Create a vivid premium event banner tailored closely to the supplied theme, with a strong visual concept and polished graphic composition.", Label = "Claude category prompt — Other", IsSensitive = false },
            new SystemSetting { Id = 17, Key = "claude_flux_category_baptism", Value = "Create a gentle, joyful and elegant Norwegian baptism banner with luminous soft colour, refined symbolic details and a premium celebratory finish.", Label = "Claude category prompt — Baptism", IsSensitive = false }
        );
    }
}
