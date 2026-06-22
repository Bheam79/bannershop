using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <summary>
    /// BANNERSH-257 — two concerns in one migration:
    ///
    /// 1.  Stop the seed-overwrite problem.
    ///     Materials and BannerSizes have been removed from <c>HasData()</c>.
    ///     EF auto-generated DELETE statements for the previously-tracked rows but we
    ///     intentionally do NOT execute them — admin-edited data must survive redeploys.
    ///     The snapshot is updated so no future migration will regenerate those deletes.
    ///
    /// 2.  Fix the default seed data to match the intended production catalogue:
    ///     •  Material names corrected; 680g is available now, 400g is future (Aug 2026).
    ///     •  Size rules reassigned: 680g gets the 154 cm height tiers (it is the current
    ///        material), 400g gets 180 cm height tiers (wider roll, available later).
    ///     •  Max widths corrected: 700 cm for 680g, 800 cm for 400g.
    ///     •  Height ranges corrected to match the actual banner catalogue.
    ///     •  300 × 180 cm standard moved to 400g (future) material.
    ///     •  267 × 150 cm standard added on 680g (the current material, 849 NOK fixed).
    /// </summary>
    public partial class FixSeedDataAndStopOverwrite : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Fix material names and availability ───────────────────────────────

            // 400g is an indoor banner not yet in production — mark future availability.
            migrationBuilder.UpdateData(
                table: "Materials",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Name", "AvailableFrom" },
                values: new object[] { "400g innendørs banner", new DateTime(2026, 8, 31, 0, 0, 0, 0, DateTimeKind.Utc) });

            // 680g is the current outdoor banner — available now (clear any future date).
            migrationBuilder.UpdateData(
                table: "Materials",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Name", "AvailableFrom" },
                values: new object[] { "680g kraftig banner - 3 år utendørs garanti", (DateTime?)null });

            // ── Reassign and correct size rules ───────────────────────────────────
            // Rules 1-3: now belong to 680g (Material 2), 154 cm height tier, max width 700.
            // Rules 4-6: now belong to 400g (Material 1), 180 cm height tier, max width 800.
            // Rule 7:    standard 300×180 moved to 400g (also future).

            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Name", "MaterialId", "MaxWidthCm", "MaxHeightCm", "PricingHeightCm" },
                values: new object[] { "680g × 154", 2, 700, 154, 154 });

            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Name", "MaterialId", "MaxWidthCm", "MinHeightCm", "MaxHeightCm", "PricingHeightCm" },
                values: new object[] { "680g × 300", 2, 700, 154, 300, 154 });

            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Name", "MaterialId", "MaxWidthCm", "MinHeightCm", "MaxHeightCm", "PricingHeightCm" },
                values: new object[] { "680g × 450", 2, 700, 300, 450, 154 });

            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Name", "MaterialId", "MaxWidthCm", "MaxHeightCm", "PricingHeightCm" },
                values: new object[] { "400g × 180", 1, 800, 180, 180 });

            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Name", "MaterialId", "MaxWidthCm", "MinHeightCm", "MaxHeightCm", "PricingHeightCm" },
                values: new object[] { "400g × 355", 1, 800, 180, 355, 180 });

            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "Name", "MaterialId", "MaxWidthCm", "MinHeightCm", "MaxHeightCm", "PricingHeightCm" },
                values: new object[] { "400g × 530", 1, 800, 360, 530, 180 });

            // 300×180 standard: move to 400g material (also future availability)
            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "MaterialId" },
                values: new object[] { 1 });

            // 267×150 standard: new row on 680g (available now), 849 NOK fixed price.
            // Use INSERT IGNORE so re-running the migration is safe.
            migrationBuilder.Sql("""
                INSERT IGNORE INTO `BannerSizes`
                  (`Name`, `IsActive`, `MaterialId`, `SortOrder`,
                   `MinWidthCm`, `MaxWidthCm`, `MinHeightCm`, `MaxHeightCm`,
                   `PricingHeightCm`, `PricingMultiplier`, `FixedPrice`)
                VALUES
                  ('267 × 150 cm -- Standard', 1, 2, 80,
                   266, 268, 149, 154,
                   154, 1, 849);
                """);

            // NOTE: No DELETE statements.  Materials and BannerSizes are no longer
            // tracked by HasData(); future migrations will not auto-generate changes
            // for these tables unless an explicit data operation is added.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the 267×150 standard row (only if it still has the expected name —
            // guards against admin renames making this delete hit the wrong row).
            migrationBuilder.Sql("""
                DELETE FROM `BannerSizes`
                WHERE `Name` = '267 × 150 cm -- Standard' AND `FixedPrice` = 849;
                """);

            // Restore 300×180 standard to 680g
            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "MaterialId" },
                values: new object[] { 2 });

            // Restore size rules 4-6 to 680g, 180cm tiers, max width 500
            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "Name", "MaterialId", "MaxWidthCm", "MinHeightCm", "MaxHeightCm", "PricingHeightCm" },
                values: new object[] { "680g — 3 panel (h 360–540)", 2, 500, 360, 540, 180 });

            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Name", "MaterialId", "MaxWidthCm", "MinHeightCm", "MaxHeightCm", "PricingHeightCm" },
                values: new object[] { "680g — 2 panel (h 180–360)", 2, 500, 180, 360, 180 });

            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Name", "MaterialId", "MaxWidthCm", "MaxHeightCm", "PricingHeightCm" },
                values: new object[] { "680g — 1 panel (h 1–180)", 2, 500, 180, 180 });

            // Restore size rules 1-3 to 400g, 154cm tiers, max width 500
            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Name", "MaterialId", "MaxWidthCm", "MinHeightCm", "MaxHeightCm", "PricingHeightCm" },
                values: new object[] { "400g — 3 panel (h 300–450)", 1, 500, 300, 450, 154 });

            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Name", "MaterialId", "MaxWidthCm", "MinHeightCm", "MaxHeightCm", "PricingHeightCm" },
                values: new object[] { "400g — 2 panel (h 154–300)", 1, 500, 154, 300, 154 });

            migrationBuilder.UpdateData(
                table: "BannerSizes",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Name", "MaterialId", "MaxWidthCm", "MaxHeightCm", "PricingHeightCm" },
                values: new object[] { "400g — 1 panel (h 1–154)", 1, 500, 154, 154 });

            // Restore material names and availability
            migrationBuilder.UpdateData(
                table: "Materials",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Name", "AvailableFrom" },
                values: new object[] { "680g Heavy Duty Banner (180cm)", new DateTime(2026, 8, 31, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Materials",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Name", "AvailableFrom" },
                values: new object[] { "400g Frontlit Banner (160cm)", (DateTime?)null });
        }
    }
}
