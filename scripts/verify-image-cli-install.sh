#!/usr/bin/env bash
# Isolated Makefile smoke: no network, Docker, systemd, or project test suite.
set -euo pipefail
source_root=${1:-$(cd "$(dirname "$0")/.." && pwd)}
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/repo/scripts" "$work/repo/frontend/dist" "$work/repo/BannerShop.Api" "$work/bin" "$work/home"
cp "$source_root/Makefile" "$work/repo/"
cp "$source_root/scripts/install-image-clis.sh" "$work/repo/scripts/"
printf 'smoke' > "$work/repo/frontend/dist/index.html"
cat > "$work/bin/npm" <<'STUB'
#!/usr/bin/env bash
set -eu
[[ "${1:-}" == install && "${2:-}" == --global ]] || exit 0
[[ "${FAIL_NPM:-0}" == 0 ]] || exit 42
prefix=$4
[[ "${GROK_HOME:-}" == "$prefix/grok-install" ]] || {
    echo 'postinstall would write outside the private prefix' >&2; exit 43;
}
package=${!#}; version=${package##*@}; package=${package%@*}; name=${package##*/}
mkdir -p "$prefix/lib/node_modules/$package" "$prefix/bin"
printf '{"version":"%s"}\n' "$version" > "$prefix/lib/node_modules/$package/package.json"
printf '#!/bin/sh\necho "%s %s"\n' "$name" "$version" > "$prefix/bin/$name"
chmod +x "$prefix/bin/$name"
echo "stub npm installed $name"
STUB
cat > "$work/bin/docker" <<'STUB'
#!/bin/sh
[ "$1" != ps ] || echo smoke-container
exit 0
STUB
for command in dotnet claude systemctl sleep; do
    printf '#!/bin/sh\nexit 0\n' > "$work/bin/$command"
done
chmod +x "$work/bin/"*
export PATH="$work/bin:$PATH" HOME="$work/home"
run_make() { make --no-print-directory -C "$work/repo" "$@"; }
# The reported symptom: unrelated preflight failure must not hide CLI setup.
if run_make up DOCKER= > "$work/missing.log" 2>&1; then
    echo 'Expected missing-Docker failure' >&2; exit 1
fi
grep -q 'Checking/installing Codex' "$work/missing.log"
grep -q 'Image CLIs ready' "$work/missing.log"
grep -q "'docker' not found" "$work/missing.log"
echo 'PASS: CLI installation is visible and completes before unrelated preflight failure'
# Whole orchestration, including generated executable paths and systemd PATH.
run_make up > "$work/up.log" 2>&1
grep -q 'BannerShop is up' "$work/up.log"
grep -q 'Keeping codex' "$work/up.log"
grep -q 'Keeping grok' "$work/up.log"
grep -q "$HOME/.local/share/bannershop/cli/bin/codex" "$HOME/.local/share/bannershop/app/appsettings.Production.json"
grep -q "$HOME/.local/share/bannershop/cli/bin:" "$HOME/.config/systemd/user/bannershop.service"
echo 'PASS: make up completes and wires private executables into production config/service'
# Installed copies must be reused even when npm cannot download anything.
FAIL_NPM=1 run_make -j4 up > "$work/repeat.log" 2>&1
test "$(grep -c 'Checking/installing Codex' "$work/repeat.log")" = 1
grep -q 'BannerShop is up' "$work/repeat.log"
echo 'PASS: parallel repeat checks once and reuses installed versions without npm'
# A real installer error must remain visible and prevent successful startup.
if FAIL_NPM=1 run_make up IMAGE_CLI_PREFIX="$work/fail-cli" > "$work/fail.log" 2>&1; then
    echo 'Expected installation failure' >&2; exit 1
fi
grep -q 'Checking/installing Codex' "$work/fail.log"
grep -q 'ERROR: Could not install codex' "$work/fail.log"
! grep -q 'BannerShop is up' "$work/fail.log"
echo 'PASS: installation failure is logged and stops startup'
