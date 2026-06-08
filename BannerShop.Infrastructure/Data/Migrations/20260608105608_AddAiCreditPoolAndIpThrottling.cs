using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiCreditPoolAndIpThrottling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AiCreditsRemaining",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HasUsedFreeAiGeneration",
                table: "Users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AiCreditTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    IpAddress = table.Column<string>(type: "varchar(45)", maxLength: 45, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Amount = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReferenceId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCreditTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiCreditTransactions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "IpAiUsages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    IpAddress = table.Column<string>(type: "varchar(45)", maxLength: 45, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpAiUsages", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "PricingParameters",
                columns: new[] { "Id", "Description", "Key", "Name", "Value" },
                values: new object[,]
                {
                    { 11, "Pris for en kreditpakke med AI forslag (NOK)", "ai_credit_pack_price_nok", "AI kreditpakke pris (NOK)", 29m },
                    { 12, "Antall AI genererings-kreditter per kreditpakke", "ai_credit_pack_count", "AI kreditpakke antall", 10m },
                    { 13, "Obligatorisk AI aktiveringsgebyr ved bestilling av banner med AI design (NOK)", "ai_banner_activation_fee_nok", "AI aktiveringsgebyr (NOK)", 95m },
                    { 14, "Antall AI kreditter som gis når AI aktiveringsgebyret er betalt", "ai_banner_activation_credits", "AI kreditter ved bestilling", 20m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiCreditTransactions_ReferenceId",
                table: "AiCreditTransactions",
                column: "ReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_AiCreditTransactions_UserId",
                table: "AiCreditTransactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_IpAiUsages_IpAddress",
                table: "IpAiUsages",
                column: "IpAddress");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiCreditTransactions");

            migrationBuilder.DropTable(
                name: "IpAiUsages");

            migrationBuilder.DeleteData(
                table: "PricingParameters",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "PricingParameters",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "PricingParameters",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "PricingParameters",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DropColumn(
                name: "AiCreditsRemaining",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "HasUsedFreeAiGeneration",
                table: "Users");
        }
    }
}
