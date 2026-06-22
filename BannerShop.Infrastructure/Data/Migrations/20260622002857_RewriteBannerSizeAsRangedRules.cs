using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <summary>
    /// BANNERSH-255 — rewrites <c>BannerSize</c> from a concrete-dimension table
    /// (WidthCm/HeightCm/IsCustomWidth/IsCustomHeight) into a range-based pricing-rules
    /// table (MinWidthCm/MaxWidthCm/MinHeightCm/MaxHeightCm + PricingHeightCm +
    /// PricingMultiplier). Seed data is replaced with three height-tier rules per
    /// material to encode banner gluing pricing.
    /// </summary>
    public partial class RewriteBannerSizeAsRangedRules : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove the seed row that previously sat at Id=100; the new seed set
            // is contiguous (1–7) and that old row has no equivalent.
            migrationBuilder.DeleteData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 100);

            // Drop the obsolete shape — these columns are gone in the new model.
            migrationBuilder.DropColumn(name: "IsCustomHeight", table: "BannerSizes");
            migrationBuilder.DropColumn(name: "IsCustomWidth",  table: "BannerSizes");
            migrationBuilder.DropColumn(name: "WidthCm",        table: "BannerSizes");
            migrationBuilder.DropColumn(name: "HeightCm",       table: "BannerSizes");

            // Add the new range + pricing-formula columns.
            migrationBuilder.AddColumn<int>(name: "MinWidthCm",        table: "BannerSizes", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "MaxWidthCm",        table: "BannerSizes", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "MinHeightCm",       table: "BannerSizes", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "MaxHeightCm",       table: "BannerSizes", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "PricingHeightCm",   table: "BannerSizes", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "PricingMultiplier", table: "BannerSizes", type: "int", nullable: false, defaultValue: 1);

            // ── Replace seed rows with the new range-based defaults ──────────────
            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "MaterialId", "Name", "SortOrder", "MinWidthCm", "MaxWidthCm", "MinHeightCm", "MaxHeightCm", "PricingHeightCm", "PricingMultiplier", "FixedPrice" },
                values: new object[] { 1, "400g — 1 panel (h 1–154)",  10, 1, 500, 1,   154, 154, 1, null });

            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "MaterialId", "Name", "SortOrder", "MinWidthCm", "MaxWidthCm", "MinHeightCm", "MaxHeightCm", "PricingHeightCm", "PricingMultiplier", "FixedPrice" },
                values: new object[] { 1, "400g — 2 panel (h 154–300)", 20, 1, 500, 154, 300, 154, 2, null });

            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "MaterialId", "Name", "SortOrder", "MinWidthCm", "MaxWidthCm", "MinHeightCm", "MaxHeightCm", "PricingHeightCm", "PricingMultiplier", "FixedPrice" },
                values: new object[] { 1, "400g — 3 panel (h 300–450)", 30, 1, 500, 300, 450, 154, 3, null });

            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "MaterialId", "Name", "SortOrder", "MinWidthCm", "MaxWidthCm", "MinHeightCm", "MaxHeightCm", "PricingHeightCm", "PricingMultiplier", "FixedPrice" },
                values: new object[] { 2, "680g — 1 panel (h 1–180)",   40, 1, 500, 1,   180, 180, 1, null });

            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "MaterialId", "Name", "SortOrder", "MinWidthCm", "MaxWidthCm", "MinHeightCm", "MaxHeightCm", "PricingHeightCm", "PricingMultiplier", "FixedPrice" },
                values: new object[] { 2, "680g — 2 panel (h 180–360)", 50, 1, 500, 180, 360, 180, 2, null });

            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "MaterialId", "Name", "SortOrder", "MinWidthCm", "MaxWidthCm", "MinHeightCm", "MaxHeightCm", "PricingHeightCm", "PricingMultiplier", "FixedPrice" },
                values: new object[] { 2, "680g — 3 panel (h 360–540)", 60, 1, 500, 360, 540, 180, 3, null });

            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "MaterialId", "Name", "SortOrder", "MinWidthCm", "MaxWidthCm", "MinHeightCm", "MaxHeightCm", "PricingHeightCm", "PricingMultiplier", "FixedPrice" },
                values: new object[] { 2, "300 × 180 cm — Standard",    70, 300, 300, 180, 180, 180, 1, 699m });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MaxHeightCm",       table: "BannerSizes");
            migrationBuilder.DropColumn(name: "MaxWidthCm",        table: "BannerSizes");
            migrationBuilder.DropColumn(name: "MinHeightCm",       table: "BannerSizes");
            migrationBuilder.DropColumn(name: "MinWidthCm",        table: "BannerSizes");
            migrationBuilder.DropColumn(name: "PricingHeightCm",   table: "BannerSizes");
            migrationBuilder.DropColumn(name: "PricingMultiplier", table: "BannerSizes");

            migrationBuilder.AddColumn<int>(name: "HeightCm",       table: "BannerSizes", type: "int",       nullable: false, defaultValue: 150);
            migrationBuilder.AddColumn<int>(name: "WidthCm",        table: "BannerSizes", type: "int",       nullable: true);
            migrationBuilder.AddColumn<bool>(name: "IsCustomHeight", table: "BannerSizes", type: "tinyint(1)", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "IsCustomWidth",  table: "BannerSizes", type: "tinyint(1)", nullable: false, defaultValue: false);

            migrationBuilder.UpdateData(table: "BannerSizes", keyColumn: "Id", keyValue: 1, columns: new[] { "MaterialId", "Name", "SortOrder", "WidthCm", "HeightCm", "IsCustomWidth", "IsCustomHeight" }, values: new object[] { 1, "300 × 150 cm", 1, 300,  150, false, false });
            migrationBuilder.UpdateData(table: "BannerSizes", keyColumn: "Id", keyValue: 2, columns: new[] { "MaterialId", "Name", "SortOrder", "WidthCm", "HeightCm", "IsCustomWidth", "IsCustomHeight" }, values: new object[] { 1, "350 × 150 cm", 2, 350,  150, false, false });
            migrationBuilder.UpdateData(table: "BannerSizes", keyColumn: "Id", keyValue: 3, columns: new[] { "MaterialId", "Name", "SortOrder", "WidthCm", "HeightCm", "IsCustomWidth", "IsCustomHeight" }, values: new object[] { 1, "400 × 150 cm", 3, 400,  150, false, false });
            migrationBuilder.UpdateData(table: "BannerSizes", keyColumn: "Id", keyValue: 4, columns: new[] { "MaterialId", "Name", "SortOrder", "WidthCm", "HeightCm", "IsCustomWidth", "IsCustomHeight" }, values: new object[] { 1, "450 × 150 cm", 4, 450,  150, false, false });
            migrationBuilder.UpdateData(table: "BannerSizes", keyColumn: "Id", keyValue: 5, columns: new[] { "MaterialId", "Name", "SortOrder", "WidthCm", "HeightCm", "IsCustomWidth", "IsCustomHeight" }, values: new object[] { 1, "500 × 150 cm", 5, 500,  150, false, false });
            migrationBuilder.UpdateData(table: "BannerSizes", keyColumn: "Id", keyValue: 6, columns: new[] { "MaterialId", "Name", "SortOrder", "WidthCm", "HeightCm", "IsCustomWidth", "IsCustomHeight" }, values: new object[] { 1, "Valgfri bredde × 150 cm", 6, null, 150, true,  false });
            migrationBuilder.UpdateData(table: "BannerSizes", keyColumn: "Id", keyValue: 7, columns: new[] { "MaterialId", "Name", "SortOrder", "WidthCm", "HeightCm", "IsCustomWidth", "IsCustomHeight" }, values: new object[] { 2, "300 × 180 cm", 7, 300,  180, false, false });
            migrationBuilder.InsertData(table: "BannerSizes", columns: new[] { "Id", "FixedPrice", "HeightCm", "IsActive", "IsCustomHeight", "IsCustomWidth", "MaterialId", "Name", "SortOrder", "WidthCm" }, values: new object[] { 100, null, 180, true, false, true, 2, "Valgfri bredde × 180 cm", 8, null });
        }
    }
}
