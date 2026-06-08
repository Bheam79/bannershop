using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEyeletsToOrderItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EyeletCount",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "EyeletFeeNok",
                table: "OrderItems",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "EyeletOption",
                table: "OrderItems",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "PricingParameters",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Description", "Key", "Name", "Value" },
                values: new object[] { "Pris per malje (NOK). Tilvalg per banner — ikke inkludert i basispris.", "eyelet_price_nok", "Maljepris (per stk)", 15m });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EyeletCount",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "EyeletFeeNok",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "EyeletOption",
                table: "OrderItems");

            migrationBuilder.UpdateData(
                table: "PricingParameters",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Description", "Key", "Name", "Value" },
                values: new object[] { "Fast avgift for hem og øyebolter - inkludert i basispris", "hem_and_eyelets_flat_fee", "Hem og øyebolter (flat avgift)", 0m });
        }
    }
}
