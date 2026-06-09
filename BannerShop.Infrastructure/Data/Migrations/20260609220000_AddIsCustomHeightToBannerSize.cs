using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIsCustomHeightToBannerSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCustomHeight",
                table: "BannerSizes",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCustomHeight",
                table: "BannerSizes");
        }
    }
}
