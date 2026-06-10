using BannerShop.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BannerShop.Infrastructure.Data.Configurations;

// BANNERSH-65: credit ledger with idempotency
public class AiCreditTransactionConfiguration : IEntityTypeConfiguration<AiCreditTransaction>
{
    public void Configure(EntityTypeBuilder<AiCreditTransaction> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.IpAddress).HasMaxLength(45);
        e.Property(x => x.Reason).HasConversion<string>().HasMaxLength(50).IsRequired();
        e.Property(x => x.ReferenceId).HasMaxLength(255);
        // Index on ReferenceId for fast idempotency look-ups.
        // Application-layer dedup (checking for existing ReferenceId) is the primary guard.
        // MySQL/MariaDB allows multiple NULLs even on UNIQUE indexes, but we keep this
        // non-unique to stay compatible with the application-level idempotency check.
        e.HasIndex(x => x.ReferenceId)
         .HasDatabaseName("IX_AiCreditTransactions_ReferenceId");
        e.HasOne(x => x.User)
            .WithMany(x => x.AiCreditTransactions)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.UserId).HasDatabaseName("IX_AiCreditTransactions_UserId");
    }
}
