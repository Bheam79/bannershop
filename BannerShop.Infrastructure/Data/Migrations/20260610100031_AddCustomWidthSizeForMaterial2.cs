using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomWidthSizeForMaterial2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "BannerSizes",
                columns: new[] { "Id", "FixedPrice", "HeightCm", "IsActive", "IsCustomHeight", "IsCustomWidth", "MaterialId", "Name", "SortOrder", "WidthCm" },
                values: new object[] { 100, null, 180, true, false, true, 2, "Valgfri bredde × 180 cm", 8, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 100);
        }
    }
}
