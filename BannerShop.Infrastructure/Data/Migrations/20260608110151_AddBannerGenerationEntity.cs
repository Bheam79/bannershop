using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBannerGenerationEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentGenerationId",
                table: "DesignRequests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BannerGenerations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    DesignRequestId = table.Column<int>(type: "int", nullable: false),
                    StoragePath = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CroppedStoragePath = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ErrorMessage = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BannerGenerations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BannerGenerations_DesignRequests_DesignRequestId",
                        column: x => x.DesignRequestId,
                        principalTable: "DesignRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_DesignRequests_CurrentGenerationId",
                table: "DesignRequests",
                column: "CurrentGenerationId");

            migrationBuilder.CreateIndex(
                name: "IX_BannerGenerations_DesignRequestId",
                table: "BannerGenerations",
                column: "DesignRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_DesignRequests_BannerGenerations_CurrentGenerationId",
                table: "DesignRequests",
                column: "CurrentGenerationId",
                principalTable: "BannerGenerations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DesignRequests_BannerGenerations_CurrentGenerationId",
                table: "DesignRequests");

            migrationBuilder.DropTable(
                name: "BannerGenerations");

            migrationBuilder.DropIndex(
                name: "IX_DesignRequests_CurrentGenerationId",
                table: "DesignRequests");

            migrationBuilder.DropColumn(
                name: "CurrentGenerationId",
                table: "DesignRequests");
        }
    }
}
