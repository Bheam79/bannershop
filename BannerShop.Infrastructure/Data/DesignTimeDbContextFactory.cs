using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BannerShop.Infrastructure.Data;

/// <summary>
/// Used only at design time (migrations) so EF Core doesn't need a running DB or full startup.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BannerShopDbContext>
{
    public BannerShopDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BannerShopDbContext>();

        // Use a dummy connection string for design-time tooling
        var connectionString = Environment.GetEnvironmentVariable("BANNERSHOP_DB")
            ?? "Server=localhost;Port=3306;Database=bannershop;User=bannershop;Password=bannershop_dev;";

        // Use a known MariaDB version to avoid connecting to DB at design time
        optionsBuilder.UseMySql(connectionString, new MariaDbServerVersion(new Version(11, 0, 0)));

        return new BannerShopDbContext(optionsBuilder.Options);
    }
}
