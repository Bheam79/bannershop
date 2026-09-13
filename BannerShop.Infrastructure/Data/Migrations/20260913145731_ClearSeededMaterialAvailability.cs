using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClearSeededMaterialAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Both materials are now in stock. Clear only the old seeded 400g date;
            // preserve availability dates explicitly changed by an administrator.
            migrationBuilder.Sql("""
                UPDATE `Materials`
                SET `AvailableFrom` = NULL
                WHERE `Id` = 1 AND `WeightGsm` = 400
                  AND `AvailableFrom` = '2026-08-31 00:00:00';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Stock has arrived: rolling back code must not reinstate an obsolete gate
            // or replace a null value that an administrator had already saved.
        }
    }
}
