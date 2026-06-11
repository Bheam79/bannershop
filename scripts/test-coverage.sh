#!/usr/bin/env bash
# ============================================================================
# test-coverage.sh — BannerShop .NET unit-test coverage
#
# Runs the xUnit tests under BannerShop.Tests with Coverlet instrumentation
# and generates an HTML report (plus Cobertura XML) via ReportGenerator.
#
# Usage:
#   ./scripts/test-coverage.sh
#
# Output:
#   ./TestResults/           raw Cobertura XML from dotnet test
#   ./coverage-report/       generated HTML report (open index.html)
#
# Requirements:
#   - dotnet SDK 10+
#   - dotnet-reportgenerator-globaltool (local manifest, auto-restored below)
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

RESULTS_DIR="$REPO_ROOT/TestResults"
REPORT_DIR="$REPO_ROOT/coverage-report"

echo ""
echo "┌──────────────────────────────────────────────────────┐"
echo "│       BannerShop — Unit Test Coverage Report         │"
echo "└──────────────────────────────────────────────────────┘"
echo ""

# ── Restore local dotnet tools (reportgenerator) ─────────────────────────────
echo "[1/3] Restoring dotnet local tools..."
cd "$REPO_ROOT"
dotnet tool restore --verbosity quiet
echo "      ✓ Tools ready"

# ── Run tests with coverage ───────────────────────────────────────────────────
echo ""
echo "[2/3] Running unit tests with coverage collection..."
rm -rf "$RESULTS_DIR"

dotnet test "$REPO_ROOT/BannerShop.slnx" \
  --collect:"XPlat Code Coverage" \
  --results-directory "$RESULTS_DIR" \
  --settings "$REPO_ROOT/coverage.runsettings" \
  --nologo

echo "      ✓ Tests complete"

# ── Find coverage XML ─────────────────────────────────────────────────────────
COVERAGE_XML=$(find "$RESULTS_DIR" -name "coverage.cobertura.xml" 2>/dev/null | head -1)

if [ -z "$COVERAGE_XML" ]; then
  echo ""
  echo "ERROR: No coverage.cobertura.xml found under $RESULTS_DIR" >&2
  echo "       Make sure coverlet.collector is referenced in BannerShop.Tests.csproj" >&2
  exit 1
fi

echo ""
echo "[3/3] Generating HTML report..."
rm -rf "$REPORT_DIR"

dotnet reportgenerator \
  -reports:"$COVERAGE_XML" \
  -targetdir:"$REPORT_DIR" \
  -reporttypes:"Html;HtmlSummary;Badges;Cobertura" \
  -sourcedirs:"$REPO_ROOT" \
  -title:"BannerShop Unit Test Coverage" \
  -verbosity:"Warning"

echo "      ✓ Report generated"
echo ""
echo "┌──────────────────────────────────────────────────────────────────────┐"
echo "│  Coverage report: file://$REPORT_DIR/index.html"
echo "└──────────────────────────────────────────────────────────────────────┘"
echo ""
