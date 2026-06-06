using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFinalBannerDesignIdToDesignRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FinalBannerDesignId",
                table: "DesignRequests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DesignRequests_FinalBannerDesignId",
                table: "DesignRequests",
                column: "FinalBannerDesignId");

            migrationBuilder.AddForeignKey(
                name: "FK_DesignRequests_BannerDesigns_FinalBannerDesignId",
                table: "DesignRequests",
                column: "FinalBannerDesignId",
                principalTable: "BannerDesigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DesignRequests_BannerDesigns_FinalBannerDesignId",
                table: "DesignRequests");

            migrationBuilder.DropIndex(
                name: "IX_DesignRequests_FinalBannerDesignId",
                table: "DesignRequests");

            migrationBuilder.DropColumn(
                name: "FinalBannerDesignId",
                table: "DesignRequests");
        }
    }
}
