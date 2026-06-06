using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderLeadTimeParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "PricingParameters",
                columns: new[] { "Id", "Description", "Key", "Name", "Value" },
                values: new object[,]
                {
                    { 9, "Produksjons- og leveringstid for standard ordre (dager fra bestilling)", "standard_lead_time_days", "Standard leveringstid (dager)", 14m },
                    { 10, "Produksjonstid for express-ordre (dager fra bestilling, før forsendelse)", "express_lead_time_days", "Express leveringstid (dager)", 3m }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PricingParameters",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "PricingParameters",
                keyColumn: "Id",
                keyValue: 10);
        }
    }
}
