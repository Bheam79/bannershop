using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSeededClaudeTokenAndAddOAuthRefresh : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "Label", "Value" },
                values: new object[] { "Claude Code OAuth token", "" });

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "IsSensitive", "Key", "Label", "Value" },
                values: new object[,]
                {
                    { 18, true, "claude_code_oauth_refresh_token", "Claude OAuth refresh token (managed)", "" },
                    { 19, true, "claude_code_oauth_expires_at", "Claude OAuth expiry (managed)", "" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 18);

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 19);

            migrationBuilder.UpdateData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "Label", "Value" },
                values: new object[] { "Claude Code long-lived OAuth token", "" });
        }
    }
}
