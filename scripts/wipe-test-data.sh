#!/usr/bin/env bash
# ============================================================================
# wipe-test-data.sh — BannerShop dev data reset
#
# Wipes ALL orders, banner designs, design requests, AI credit history,
# and uploaded/generated files from the dev environment.
#
# Preserves: Users, Addresses, RefreshTokens, PricingParameters, Materials,
#            BannerSizes, BannerTemplates, SystemSettings.
# Resets:    User.AiCreditsRemaining → 0 and User.HasUsedFreeAiGeneration → 0.
#
# Usage:
#   ./scripts/wipe-test-data.sh [--yes]
#
#   --yes   Skip the confirmation prompt (for CI / scripted use)
#
# Database connection:
#   The script reads the DB host/port/name/user/password from the running
#   MariaDB container via `cb docker` and from the hard-coded dev credentials.
#   Override individual values with environment variables:
#     DB_HOST  DB_PORT  DB_NAME  DB_USER  DB_PASS
#
# File storage:
#   Wipes the directory pointed to by FileStorage:LocalRoot in appsettings.json
#   (default: /workspace/uploads). Override with UPLOADS_DIR env var.
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# ── Defaults ────────────────────────────────────────────────────────────────
DB_NAME="${DB_NAME:-bannershop}"
DB_USER="${DB_USER:-bannershop}"
DB_PASS="${DB_PASS:-bannershop_dev}"
DB_PORT="${DB_PORT:-3306}"
UPLOADS_DIR="${UPLOADS_DIR:-/workspace/uploads}"

# ── Resolve DB host ──────────────────────────────────────────────────────────
if [[ -z "${DB_HOST:-}" ]]; then
  # Try to get the IP of the running 'db' sibling container.
  if command -v cb &>/dev/null && cb docker ps 2>/dev/null | grep -q '\bdb\b'; then
    DB_HOST="$(cb docker exec db hostname -I 2>/dev/null | tr -d ' ' || true)"
  fi
  # Fall back to the IP currently in appsettings.json
  if [[ -z "${DB_HOST:-}" ]]; then
    APPSETTINGS="$REPO_ROOT/BannerShop.Api/appsettings.json"
    if [[ -f "$APPSETTINGS" ]]; then
      DB_HOST="$(grep -oP '(?<=Server=)[^;]+' "$APPSETTINGS" | head -1 || true)"
    fi
  fi
  if [[ -z "${DB_HOST:-}" ]]; then
    echo "ERROR: Could not determine DB host. Set DB_HOST env var or start the db container." >&2
    exit 1
  fi
fi

echo ""
echo "┌──────────────────────────────────────────────────────┐"
echo "│        BannerShop — DEV DATA WIPE SCRIPT             │"
echo "├──────────────────────────────────────────────────────┤"
echo "│  Database : $DB_USER@$DB_HOST:$DB_PORT/$DB_NAME"
echo "│  Uploads  : $UPLOADS_DIR"
echo "└──────────────────────────────────────────────────────┘"
echo ""
echo "This will PERMANENTLY DELETE all of the following:"
echo "  • Orders, OrderItems, ProductionStatuses, ShipmentTrackings"
echo "  • DesignRequests, DesignRequestRevisions, BannerGenerations"
echo "  • BannerDesigns"
echo "  • AiCreditTransactions, IpAiUsages"
echo "  • All files under: $UPLOADS_DIR/"
echo "  • Reset user AI credits and free-generation flag"
echo ""
echo "The following are NOT touched:"
echo "  • User accounts, addresses, refresh tokens"
echo "  • Pricing parameters, materials, banner sizes, templates"
echo "  • System settings (API keys, etc.)"
echo ""

# ── Confirmation ─────────────────────────────────────────────────────────────
SKIP_CONFIRM=0
for arg in "$@"; do
  [[ "$arg" == "--yes" ]] && SKIP_CONFIRM=1
done

if [[ $SKIP_CONFIRM -eq 0 ]]; then
  read -r -p "Type YES to proceed: " CONFIRM
  if [[ "$CONFIRM" != "YES" ]]; then
    echo "Aborted."
    exit 0
  fi
fi

# ── MySQL helper ──────────────────────────────────────────────────────────────
mysql_exec() {
  mysql \
    --host="$DB_HOST" \
    --port="$DB_PORT" \
    --user="$DB_USER" \
    --password="$DB_PASS" \
    --database="$DB_NAME" \
    --batch \
    --silent \
    -e "$1"
}

echo ""
echo "[1/4] Verifying database connection..."
mysql_exec "SELECT 1;" > /dev/null
echo "      ✓ Connected to $DB_HOST:$DB_PORT/$DB_NAME"

# ── SQL wipe ──────────────────────────────────────────────────────────────────
echo ""
echo "[2/4] Wiping database rows..."

mysql_exec "
-- Disable FK checks so we can delete in any order without worrying about
-- constraint violations from circular or cross-table references.
SET FOREIGN_KEY_CHECKS = 0;

-- ── Break circular / cross references ──
--   DesignRequest.CurrentGenerationId   → BannerGenerations (SetNull)
--   DesignRequest.FinalBannerDesignId   → BannerDesigns     (Restrict → must null first)
--   DesignRequest.OrderId               → Orders            (SetNull)
--   OrderItem.BannerDesignId            → BannerDesigns     (Restrict → must null first)
--   OrderItem.DesignRequestId           → DesignRequests    (Restrict → must null first)

UPDATE DesignRequests SET CurrentGenerationId = NULL;
UPDATE DesignRequests SET FinalBannerDesignId = NULL;
UPDATE DesignRequests SET OrderId = NULL;
UPDATE OrderItems     SET BannerDesignId   = NULL;
UPDATE OrderItems     SET DesignRequestId  = NULL;

-- ── Delete child rows (cascade-eligible but safer to be explicit) ──
DELETE FROM BannerGenerations;
DELETE FROM DesignRequestRevisions;
DELETE FROM ProductionStatuses;
DELETE FROM ShipmentTrackings;
DELETE FROM OrderItems;

-- ── Delete top-level transactional rows ──
DELETE FROM Orders;
DELETE FROM DesignRequests;
DELETE FROM BannerDesigns;

-- ── Delete AI credit / throttling records ──
DELETE FROM AiCreditTransactions;
DELETE FROM IpAiUsages;

-- ── Reset per-user AI state ──
UPDATE Users SET AiCreditsRemaining = 0, HasUsedFreeAiGeneration = 0;

SET FOREIGN_KEY_CHECKS = 1;
"

echo "      ✓ All transactional rows deleted"

# ── Row count sanity check ────────────────────────────────────────────────────
echo ""
echo "      Post-wipe row counts:"
mysql_exec "
SELECT 'Orders'               AS tbl, COUNT(*) AS rows FROM Orders
UNION ALL SELECT 'OrderItems',           COUNT(*) FROM OrderItems
UNION ALL SELECT 'ProductionStatuses',   COUNT(*) FROM ProductionStatuses
UNION ALL SELECT 'ShipmentTrackings',    COUNT(*) FROM ShipmentTrackings
UNION ALL SELECT 'DesignRequests',       COUNT(*) FROM DesignRequests
UNION ALL SELECT 'DesignRequestRevisions', COUNT(*) FROM DesignRequestRevisions
UNION ALL SELECT 'BannerGenerations',    COUNT(*) FROM BannerGenerations
UNION ALL SELECT 'BannerDesigns',        COUNT(*) FROM BannerDesigns
UNION ALL SELECT 'AiCreditTransactions', COUNT(*) FROM AiCreditTransactions
UNION ALL SELECT 'IpAiUsages',           COUNT(*) FROM IpAiUsages
UNION ALL SELECT 'Users (unchanged)',    COUNT(*) FROM Users;
" | column -t | sed 's/^/      /'

# ── File storage wipe ─────────────────────────────────────────────────────────
echo ""
echo "[3/4] Wiping uploaded / generated files..."

if [[ -d "$UPLOADS_DIR" ]]; then
  FILE_COUNT=$(find "$UPLOADS_DIR" -type f 2>/dev/null | wc -l)
  if [[ "$FILE_COUNT" -gt 0 ]]; then
    # Remove everything inside but keep the directory itself so the app can
    # still write new uploads without needing to recreate the root folder.
    find "$UPLOADS_DIR" -mindepth 1 -delete
    echo "      ✓ Deleted $FILE_COUNT file(s) from $UPLOADS_DIR/"
  else
    echo "      ✓ Uploads directory already empty"
  fi
else
  echo "      ⚠  Uploads directory not found: $UPLOADS_DIR (nothing to delete)"
fi

# ── Done ──────────────────────────────────────────────────────────────────────
echo ""
echo "[4/4] Done."
echo ""
echo "  The database is clean. You can now restart the API and test from scratch."
echo "  (User accounts and settings are intact.)"
echo ""
