using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingPricingParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "PricingParameters",
                columns: new[] { "Id", "Description", "Key", "Name", "Value" },
                values: new object[,]
                {
                    { 6, "Estimert diameter på rullet banner-tube for forsendelse (cm)", "shipping_tube_diameter_cm", "Forsendelse: rull-diameter (cm)", 15m },
                    { 7, "Vekt av emballasje (tube, lokk, etiketter) i gram", "shipping_packaging_weight_g", "Forsendelse: emballasjevekt (g)", 500m },
                    { 8, "Maks tube-lengde transportør aksepterer (cm) — Bring Servicepakke", "shipping_max_length_cm", "Forsendelse: maks lengde (cm)", 240m }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PricingParameters",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "PricingParameters",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "PricingParameters",
                keyColumn: "Id",
                keyValue: 8);
        }
    }
}
