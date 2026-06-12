#!/usr/bin/env bash
# ============================================================================
# wipe-test-data.sh — BannerShop dev/prod data reset
#
# Wipes ALL orders, banner designs, design requests, AI credit history,
# and uploaded/generated files from the environment.
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
# Database connection — resolved in this order (first match wins):
#   1. Explicit env vars: DB_HOST, DB_PORT, DB_USER, DB_PASS
#   2. Production layout:
#        container named 'bannershop-db' is running AND
#        ~/.local/share/bannershop/secrets/db_password exists
#        → connect to 127.0.0.1:17006 using that password
#   3. Dev container (cb docker available):
#        container named 'db' running → connect via its internal IP on 3306
#   4. Parse Server= and Port= from the closest appsettings*.json
#
# File storage — resolved in this order:
#   1. UPLOADS_DIR env var (explicit override)
#   2. ~/.local/share/bannershop/data/uploads   (production layout, if present)
#   3. /workspace/uploads                        (dev container default)
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# ── DB credential defaults (overrideable) ────────────────────────────────────
DB_NAME="${DB_NAME:-bannershop}"
DB_USER="${DB_USER:-bannershop}"
DB_PASS="${DB_PASS:-bannershop_dev}"
# DB_PORT is intentionally left unset here so the detection branches below can
# choose the right default (17006 for production, 3306 for dev).
# It is finalised to 3306 at the end of the detection block if nothing else set it.

# ── Resolve DB host + port + password ────────────────────────────────────────
if [[ -z "${DB_HOST:-}" ]]; then

  # ── Option 1: Production layout ─────────────────────────────────────────────
  # bannershop-db container running AND secrets dir present.
  # In production the container is bound to 127.0.0.1:17006 on the host, so
  # connecting to the container's internal IP is wrong — always use localhost.
  PROD_SECRETS="$HOME/.local/share/bannershop/secrets/db_password"
  if (docker ps --format '{{.Names}}' 2>/dev/null || podman ps --format '{{.Names}}' 2>/dev/null) \
       | grep -qx 'bannershop-db' 2>/dev/null \
     && [[ -f "$PROD_SECRETS" ]]; then
    DB_HOST="127.0.0.1"
    DB_PORT="${DB_PORT:-17006}"
    DB_PASS="$(cat "$PROD_SECRETS")"
    echo "  Detected production setup (bannershop-db container + secrets dir)."
    echo "  DB_HOST=127.0.0.1  DB_PORT=$DB_PORT"

  # ── Option 2: Dev container via cb docker ───────────────────────────────────
  elif command -v cb &>/dev/null && cb docker ps 2>/dev/null | grep -q '\bdb\b'; then
    DB_HOST="$(cb docker exec db hostname -I 2>/dev/null | tr -d ' ' || true)"
    DB_PORT="${DB_PORT:-3306}"
    echo "  Detected dev container (cb docker 'db')."
    echo "  DB_HOST=$DB_HOST  DB_PORT=$DB_PORT"
  fi

  # ── Option 3: Parse appsettings*.json ───────────────────────────────────────
  if [[ -z "${DB_HOST:-}" ]]; then
    # Try Production config first (deployed app dir), then source-tree fallback.
    for CFG in \
      "$HOME/.local/share/bannershop/app/appsettings.Production.json" \
      "$REPO_ROOT/BannerShop.Api/appsettings.json"; do
      if [[ -f "$CFG" ]]; then
        _h="$(grep -oP '(?<=Server=)[^;\"]+' "$CFG" 2>/dev/null | head -1 || true)"
        _p="$(grep -oP '(?<=Port=)[0-9]+' "$CFG" 2>/dev/null | head -1 || true)"
        if [[ -n "$_h" ]]; then
          DB_HOST="$_h"
          [[ -n "$_p" ]] && DB_PORT="$_p"
          echo "  Parsed DB host from: $CFG"
          echo "  DB_HOST=$DB_HOST  DB_PORT=$DB_PORT"
          break
        fi
      fi
    done
  fi

  if [[ -z "${DB_HOST:-}" ]]; then
    echo ""
    echo "ERROR: Could not determine DB host." >&2
    echo "  Set DB_HOST (and optionally DB_PORT, DB_PASS) as environment variables." >&2
    echo "  Examples:" >&2
    echo "    DB_HOST=127.0.0.1 DB_PORT=17006 DB_PASS=\$(cat ~/.local/share/bannershop/secrets/db_password) $0" >&2
    echo "    DB_HOST=10.89.7.5 $0" >&2
    exit 1
  fi
fi

# Finalise DB_PORT if still unset (no explicit env var, no production/dev detection hit)
DB_PORT="${DB_PORT:-3306}"

# ── Resolve uploads directory ─────────────────────────────────────────────────
if [[ -z "${UPLOADS_DIR:-}" ]]; then
  PROD_UPLOADS="$HOME/.local/share/bannershop/data/uploads"
  if [[ -d "$PROD_UPLOADS" ]]; then
    UPLOADS_DIR="$PROD_UPLOADS"
  else
    UPLOADS_DIR="/workspace/uploads"
  fi
fi

# ── Banner ────────────────────────────────────────────────────────────────────
echo ""
echo "┌──────────────────────────────────────────────────────┐"
echo "│        BannerShop — DEV DATA WIPE SCRIPT             │"
echo "├──────────────────────────────────────────────────────┤"
printf "│  Database : %s@%s:%s/%s\n" "$DB_USER" "$DB_HOST" "$DB_PORT" "$DB_NAME"
printf "│  Uploads  : %s\n" "$UPLOADS_DIR"
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
# Try the 'mysql' binary first; fall back to running it inside the container
# (useful on hosts where the mysql client is not installed but docker exec works).
_mysql_cmd=""
if command -v mysql &>/dev/null; then
  _mysql_cmd="mysql"
elif command -v mariadb &>/dev/null; then
  _mysql_cmd="mariadb"
fi

mysql_exec() {
  local sql="$1"
  if [[ -n "$_mysql_cmd" ]]; then
    "$_mysql_cmd" \
      --host="$DB_HOST" \
      --port="$DB_PORT" \
      --user="$DB_USER" \
      --password="$DB_PASS" \
      --database="$DB_NAME" \
      --batch \
      --silent \
      -e "$sql"
  else
    # No local mysql/mariadb binary — run inside the container.
    # Prefer bannershop-db (production), fall back to 'db' (dev container).
    local cname=""
    for c in bannershop-db db; do
      if (docker ps --format '{{.Names}}' 2>/dev/null || podman ps --format '{{.Names}}' 2>/dev/null) \
           | grep -qx "$c" 2>/dev/null; then
        cname="$c"
        break
      fi
    done
    if [[ -z "$cname" ]]; then
      echo "ERROR: No mysql/mariadb binary found and no running container to exec into." >&2
      exit 1
    fi
    (docker exec -i "$cname" mariadb \
      --user="$DB_USER" \
      --password="$DB_PASS" \
      --database="$DB_NAME" \
      --batch \
      --silent \
      -e "$sql") 2>/dev/null || \
    (docker exec -i "$cname" mysql \
      --user="$DB_USER" \
      --password="$DB_PASS" \
      --database="$DB_NAME" \
      --batch \
      --silent \
      -e "$sql")
  fi
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
SELECT 'Orders'               AS tbl, COUNT(*) AS cnt FROM Orders
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
