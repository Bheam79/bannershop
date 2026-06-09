using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitAiCreditPacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename + update small pack (was "10 credits", now 5 credits)
            migrationBuilder.UpdateData(
                table: "PricingParameters",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "Name", "Description" },
                values: new object[] {
                    "AI kreditpakke liten pris (NOK)",
                    "Pris for liten kreditpakke med AI forslag (NOK)"
                });

            migrationBuilder.UpdateData(
                table: "PricingParameters",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "Name", "Description", "Value" },
                values: new object[] {
                    "AI kreditpakke liten antall",
                    "Antall AI genererings-kreditter per liten kreditpakke",
                    5m
                });

            // Insert large pack tier
            migrationBuilder.InsertData(
                table: "PricingParameters",
                columns: new[] { "Id", "Name", "Key", "Value", "Description" },
                values: new object[] {
                    16,
                    "AI kreditpakke stor pris (NOK)",
                    "ai_credit_pack_large_price_nok",
                    95m,
                    "Pris for stor kreditpakke med AI forslag (NOK)"
                });

            migrationBuilder.InsertData(
                table: "PricingParameters",
                columns: new[] { "Id", "Name", "Key", "Value", "Description" },
                values: new object[] {
                    17,
                    "AI kreditpakke stor antall",
                    "ai_credit_pack_large_count",
                    20m,
                    "Antall AI genererings-kreditter per stor kreditpakke"
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PricingParameters",
                keyColumn: "Id",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "PricingParameters",
                keyColumn: "Id",
                keyValue: 17);

            migrationBuilder.UpdateData(
                table: "PricingParameters",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "Name", "Description" },
                values: new object[] {
                    "AI kreditpakke pris (NOK)",
                    "Pris for en kreditpakke med AI forslag (NOK)"
                });

            migrationBuilder.UpdateData(
                table: "PricingParameters",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "Name", "Description", "Value" },
                values: new object[] {
                    "AI kreditpakke antall",
                    "Antall AI genererings-kreditter per kreditpakke",
                    10m
                });
        }
    }
}
