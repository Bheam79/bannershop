using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxBannerWidthAndPanelOverlap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxBannerWidthCm",
                table: "Materials",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "Materials",
                keyColumn: "Id",
                keyValue: 1,
                column: "MaxBannerWidthCm",
                value: 160);

            migrationBuilder.UpdateData(
                table: "Materials",
                keyColumn: "Id",
                keyValue: 2,
                column: "MaxBannerWidthCm",
                value: 180);

            // Backfill MaxBannerWidthCm for any user-added materials with the roll width.
            // PricingService also has a runtime fallback, but persisting the value makes
            // the data self-describing.
            migrationBuilder.Sql(
                "UPDATE `Materials` SET `MaxBannerWidthCm` = `WidthCm` WHERE `MaxBannerWidthCm` = 0;");

            migrationBuilder.InsertData(
                table: "PricingParameters",
                columns: new[] { "Id", "Description", "Key", "Name", "Value" },
                values: new object[] { 15, "Overlapp i cm mellom panel ved sammenliming av brede banner. Bestemmer pris-multiplikator (×2, ×3, …) når bestilt bredde overstiger materialets maks bredde.", "banner_panel_overlap_cm", "Panel-overlapp (cm)", 5m });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PricingParameters",
                keyColumn: "Id",
                keyValue: 15);

            migrationBuilder.DropColumn(
                name: "MaxBannerWidthCm",
                table: "Materials");
        }
    }
}
