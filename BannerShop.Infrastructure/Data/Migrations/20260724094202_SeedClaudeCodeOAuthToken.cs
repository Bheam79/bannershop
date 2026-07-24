using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedClaudeCodeOAuthToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 8,
                column: "Value",
                value: "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");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 8,
                column: "Value",
                value: "");
        }
    }
}
