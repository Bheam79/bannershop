using BannerShop.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BannerShop.Infrastructure.Data.Configurations;

public class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Line1).HasMaxLength(200).IsRequired();
        e.Property(x => x.Line2).HasMaxLength(200);
        e.Property(x => x.PostalCode).HasMaxLength(20).IsRequired();
        e.Property(x => x.City).HasMaxLength(100).IsRequired();
        e.Property(x => x.Country).HasMaxLength(10).HasDefaultValue("NO");
        e.HasOne(x => x.User)
            .WithMany(x => x.Addresses)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
