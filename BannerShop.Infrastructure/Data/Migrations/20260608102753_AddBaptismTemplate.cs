using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBaptismTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "BannerTemplates",
                columns: new[] { "Id", "Category", "NameEn", "NameNb", "SortOrder" },
                values: new object[] { 8, "Baptism", "Baptism", "Dåp", 15 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "BannerTemplates",
                keyColumn: "Id",
                keyValue: 8);
        }
    }
}
