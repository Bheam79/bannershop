using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBannerPriceToManualDesignRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BannerPriceNok",
                table: "DesignRequests",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "BannerSizeId",
                table: "DesignRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomBannerWidthCm",
                table: "DesignRequests",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BannerPriceNok",
                table: "DesignRequests");

            migrationBuilder.DropColumn(
                name: "BannerSizeId",
                table: "DesignRequests");

            migrationBuilder.DropColumn(
                name: "CustomBannerWidthCm",
                table: "DesignRequests");
        }
    }
}
