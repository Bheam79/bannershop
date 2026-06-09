using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BannerShop.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MigrateDesignRequestsToOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Data migration: create an Order row for every DesignRequest ──────────
            //
            // Strategy: add a temporary helper column `_mig_dr_id` to Orders so we can
            // JOIN back on it without a cursor. The column is added + dropped entirely
            // inside this migration — the EF model never sees it.
            //
            // OrderType mapping:
            //   'Ai'     → 1 (AiBanner)
            //   'Manual' → 2 (ManualDesign)
            //
            // OrderState mapping:
            //   Pending      + PI set   → 1 (Paid)   [PI created means payment was attempted]
            //   Pending      + no PI    → 0 (Draft)
            //   InProgress              → 1 (Paid)
            //   AwaitingApproval Manual → 2 (DesignReady)
            //   AwaitingApproval AI     → 3 (CustomerApproval)
            //   Approved                → 4 (InProduction)
            //   RevisionRequested       → 3 (CustomerApproval)
            //   Revised       Manual    → 2 (DesignReady)
            //   Revised       AI        → 3 (CustomerApproval)
            //   Final                   → 6 (Delivered)
            //   Failed                  → 1 (Paid)   [pipeline failed but payment was made]
            //   Cancelled               → 7 (Cancelled)
            //
            // Only DesignRequests with a non-null UserId get an Order row; anonymous
            // free-tier AI requests (UserId IS NULL) are left with OrderId = NULL.

            // 1. Add temp helper column.
            migrationBuilder.Sql(
                "ALTER TABLE `Orders` ADD COLUMN `_mig_dr_id` INT NULL;");

            // 2. Bulk-insert Orders from DesignRequests (authenticated rows only).
            migrationBuilder.Sql(@"
INSERT INTO `Orders`
    (`UserId`, `Status`, `OrderType`, `OrderState`, `DeliveryType`,
     `ShippingCostNok`, `ExpressFeeNok`, `AiActivationFeeNok`, `TotalNok`,
     `StripePaymentIntentId`, `CreatedAt`, `UpdatedAt`, `_mig_dr_id`)
SELECT
    dr.`UserId`,
    'Draft' AS `Status`,
    CASE dr.`Mode`
        WHEN 'Ai'     THEN 1
        WHEN 'Manual' THEN 2
        ELSE 0
    END AS `OrderType`,
    CASE dr.`Status`
        WHEN 'Pending' THEN
            CASE WHEN dr.`StripePaymentIntentId` IS NOT NULL THEN 1 ELSE 0 END
        WHEN 'InProgress'        THEN 1
        WHEN 'AwaitingApproval'  THEN
            CASE WHEN dr.`Mode` = 'Manual' THEN 2 ELSE 3 END
        WHEN 'Approved'          THEN 4
        WHEN 'RevisionRequested' THEN 3
        WHEN 'Revised'           THEN
            CASE WHEN dr.`Mode` = 'Manual' THEN 2 ELSE 3 END
        WHEN 'Final'             THEN 6
        WHEN 'Failed'            THEN 1
        WHEN 'Cancelled'         THEN 7
        ELSE 0
    END AS `OrderState`,
    'Standard' AS `DeliveryType`,
    0 AS `ShippingCostNok`,
    0 AS `ExpressFeeNok`,
    0 AS `AiActivationFeeNok`,
    COALESCE(dr.`PriceNok`, 0) + COALESCE(dr.`BannerPriceNok`, 0) AS `TotalNok`,
    NULL AS `StripePaymentIntentId`,
    dr.`CreatedAt`,
    dr.`CreatedAt`,
    dr.`Id` AS `_mig_dr_id`
FROM `DesignRequests` dr
WHERE dr.`UserId` IS NOT NULL;
");

            // 3. Populate DesignRequest.OrderId via the helper column.
            migrationBuilder.Sql(@"
UPDATE `DesignRequests` dr
JOIN `Orders` o ON o.`_mig_dr_id` = dr.`Id`
SET dr.`OrderId` = o.`Id`;
");

            // 4. Drop the temp helper column.
            migrationBuilder.Sql(
                "ALTER TABLE `Orders` DROP COLUMN `_mig_dr_id`;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse: NULL out the FK, then delete the Orders that were created by this migration.
            // We identify them by the fact that they have no OrderItems (design-request-origin orders
            // are bare Order rows with no items yet) — this is a best-effort rollback.
            // In production, prefer a point-in-time restore over running Down().
            migrationBuilder.Sql(@"
UPDATE `DesignRequests` SET `OrderId` = NULL
WHERE `OrderId` IS NOT NULL;
");
            migrationBuilder.Sql(@"
DELETE FROM `Orders`
WHERE `Id` NOT IN (SELECT DISTINCT `OrderId` FROM `OrderItems`);
");
        }
    }
}
