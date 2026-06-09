using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "IsSensitive", "Key", "Label", "Value" },
                values: new object[,]
                {
                    { 4, true, "stripe_secret_key", "Stripe Secret Key (sk_live_… / sk_test_… / rk_live_… / rk_test_…)", "" },
                    { 5, false, "stripe_publishable_key", "Stripe Publishable Key (pk_live_… / pk_test_…)", "" },
                    { 6, true, "stripe_webhook_secret", "Stripe Webhook Secret (whsec_…)", "" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: 6);
        }
    }
}
