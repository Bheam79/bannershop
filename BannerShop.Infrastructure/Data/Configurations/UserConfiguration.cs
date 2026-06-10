using BannerShop.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BannerShop.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Email).HasMaxLength(255).IsRequired();
        e.HasIndex(x => x.Email).IsUnique();
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.Property(x => x.Phone).HasMaxLength(50);
        e.Property(x => x.Role).HasConversion<string>();
        e.Property(x => x.AiCreditsRemaining).HasDefaultValue(0);
        e.Property(x => x.HasUsedFreeAiGeneration).HasDefaultValue(false);
    }
}
