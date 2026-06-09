using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPackingModeToOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PackingMode",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PackingMode",
                table: "Orders");
        }
    }
}
