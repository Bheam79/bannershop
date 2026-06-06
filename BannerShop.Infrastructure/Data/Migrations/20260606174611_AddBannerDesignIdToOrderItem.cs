using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBannerDesignIdToOrderItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BannerDesignId",
                table: "OrderItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_BannerDesignId",
                table: "OrderItems",
                column: "BannerDesignId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_BannerDesigns_BannerDesignId",
                table: "OrderItems",
                column: "BannerDesignId",
                principalTable: "BannerDesigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_BannerDesigns_BannerDesignId",
                table: "OrderItems");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_BannerDesignId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "BannerDesignId",
                table: "OrderItems");
        }
    }
}
