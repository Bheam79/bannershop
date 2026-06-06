using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBannerTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BannerTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Category = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NameNb = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NameEn = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BannerTemplates", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "BannerTemplates",
                columns: new[] { "Id", "Category", "NameEn", "NameNb", "SortOrder" },
                values: new object[,]
                {
                    { 1, "Birthday", "Birthday", "Bursdag", 10 },
                    { 2, "Confirmation", "Confirmation", "Konfirmasjon", 20 },
                    { 3, "Wedding", "Wedding", "Bryllup", 30 },
                    { 4, "Anniversary", "Anniversary", "Jubileum", 40 },
                    { 5, "Christmas", "Christmas party", "Julefeiring", 50 },
                    { 6, "NewYear", "New Year's party", "Nyttårsfeiring", 60 },
                    { 7, "Other", "Other occasion", "Annen feiring", 70 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_BannerTemplates_SortOrder",
                table: "BannerTemplates",
                column: "SortOrder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BannerTemplates");
        }
    }
}
