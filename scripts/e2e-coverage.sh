#!/usr/bin/env bash
# ============================================================================
# e2e-coverage.sh — BannerShop Playwright e2e coverage
#
# Starts the Vite dev server with Istanbul instrumentation enabled, runs all
# Playwright tests, and generates an HTML coverage report for the frontend.
#
# Usage:
#   ./scripts/e2e-coverage.sh [extra playwright flags...]
#
#   examples:
#     ./scripts/e2e-coverage.sh
#     ./scripts/e2e-coverage.sh --grep "Public shop"
#
# Pre-requisites:
#   1. The BannerShop API backend must already be running on localhost:5000.
#      (run:  cd BannerShop.Api && dotnet run)
#   2. Dependencies installed:
#        cd frontend && npm install
#        cd e2e && npm install
#
# Output:
#   e2e/coverage/index.html   — HTML coverage report for the Vue/TS frontend
#
# Environment:
#   BASE_URL   (default http://localhost:5173) — URL Playwright navigates to
#   API_URL    (default http://localhost:5000) — backend URL used by helpers
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
FRONTEND_DIR="$REPO_ROOT/frontend"
E2E_DIR="$REPO_ROOT/e2e"

echo ""
echo "┌──────────────────────────────────────────────────────┐"
echo "│       BannerShop — E2E Frontend Coverage Report      │"
echo "└──────────────────────────────────────────────────────┘"
echo ""

# ── Ensure deps are installed ─────────────────────────────────────────────────
echo "[1/4] Checking dependencies..."
(cd "$FRONTEND_DIR" && npm install --silent)
(cd "$E2E_DIR"      && npm install --silent)
echo "      ✓ Dependencies ready"

# ── Clean previous coverage data ─────────────────────────────────────────────
echo ""
echo "[2/4] Cleaning previous coverage data..."
rm -rf "$E2E_DIR/.nyc_output" "$E2E_DIR/coverage"
echo "      ✓ Clean"

# ── Start instrumented Vite dev server ───────────────────────────────────────
echo ""
echo "[3/4] Starting Vite dev server with VITE_COVERAGE=true..."
VITE_COVERAGE=true npx --prefix "$FRONTEND_DIR" vite --port 5173 &
VITE_PID=$!

# Trap to ensure Vite is killed on exit
trap 'kill "$VITE_PID" 2>/dev/null || true' EXIT INT TERM

# Wait for Vite to be ready
WAIT_SECS=30
for i in $(seq 1 $WAIT_SECS); do
  if curl -s -o /dev/null "http://localhost:5173/"; then
    echo "      ✓ Vite ready (${i}s)"
    break
  fi
  if [ "$i" -eq "$WAIT_SECS" ]; then
    echo "ERROR: Vite did not start within ${WAIT_SECS}s" >&2
    exit 1
  fi
  sleep 1
done

# ── Run Playwright tests with coverage ───────────────────────────────────────
echo ""
echo "[4/4] Running Playwright tests..."
VITE_COVERAGE=true npx --prefix "$E2E_DIR" playwright test "$@" || true
# (failures are noted but we still want to generate the report)

echo ""
echo "┌──────────────────────────────────────────────────────────────────────┐"
echo "│  E2E coverage report: file://$E2E_DIR/coverage/index.html"
echo "└──────────────────────────────────────────────────────────────────────┘"
echo ""
