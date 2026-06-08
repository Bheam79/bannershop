using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiActivationFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AiActivationFeeNok",
                table: "Orders",
                type: "decimal(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "DesignRequestId",
                table: "OrderItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_DesignRequestId",
                table: "OrderItems",
                column: "DesignRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_DesignRequests_DesignRequestId",
                table: "OrderItems",
                column: "DesignRequestId",
                principalTable: "DesignRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_DesignRequests_DesignRequestId",
                table: "OrderItems");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_DesignRequestId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "AiActivationFeeNok",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DesignRequestId",
                table: "OrderItems");
        }
    }
}
