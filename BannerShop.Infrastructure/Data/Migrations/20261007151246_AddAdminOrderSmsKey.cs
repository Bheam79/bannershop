using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminOrderSmsKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CLI OAuth settings are created at runtime and can already occupy ID 20.
            // Allocate an ID instead, and preserve any previously configured SMS key.
            // Fix this migration itself: a later migration cannot repair a failed Up.
            migrationBuilder.Sql("""
                INSERT INTO `SystemSettings` (`IsSensitive`, `Key`, `Label`, `Value`)
                SELECT TRUE, 'admin_order_sms_key', 'Admin new-order SMS gateway key', 'mahal143'
                WHERE NOT EXISTS (
                    SELECT 1 FROM `SystemSettings` WHERE `Key` = 'admin_order_sms_key'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM `SystemSettings` WHERE `Key` = 'admin_order_sms_key';");
        }
    }
}
