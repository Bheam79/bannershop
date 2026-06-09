using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderTypeStateAndDesignRequestOrderFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "OrderState",
                table: "Orders",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<byte>(
                name: "OrderType",
                table: "Orders",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)0);

            // Backfill: all pre-existing orders were paid custom-banner orders.
            // OrderType = 0 (CustomBanner) is already the column default so no UPDATE is needed.
            // OrderState defaults to 0 (Draft) from the column definition, so backfill to 1 (Paid).
            migrationBuilder.Sql("UPDATE `Orders` SET `OrderState` = 1 WHERE `OrderState` = 0;");

            migrationBuilder.AddColumn<int>(
                name: "OrderId",
                table: "DesignRequests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DesignRequests_OrderId",
                table: "DesignRequests",
                column: "OrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_DesignRequests_Orders_OrderId",
                table: "DesignRequests",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DesignRequests_Orders_OrderId",
                table: "DesignRequests");

            migrationBuilder.DropIndex(
                name: "IX_DesignRequests_OrderId",
                table: "DesignRequests");

            migrationBuilder.DropColumn(
                name: "OrderState",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "OrderType",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "DesignRequests");
        }
    }
}
