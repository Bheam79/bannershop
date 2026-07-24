using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFalAiImageProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "IsSensitive", "Key", "Label", "Value" },
                values: new object[] { 7, true, "fal_api_key", "fal.ai API Key", "e708f826-b18c-44cb-9c57-242fc1aafff8:68b9a036f3d627c891f947e71bec479a" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "IsSensitive", "Key", "Label", "Value" },
                values: new object[,]
                {
                    { 2, false, "openai_image_model", "OpenAI Image Model (blank = use config default)", "" },
                    { 3, false, "openai_image_quality", "OpenAI Image Quality (blank = use config default; allowed: low, medium, high, auto)", "" }
                });
        }
    }
}
