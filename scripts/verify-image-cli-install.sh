#!/usr/bin/env bash
# Isolated deployment smoke: no network, Docker, systemd, or project test suite.
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
cat > "$work/bin/loginctl" <<'STUB'
#!/usr/bin/env bash
set -eu
case "$*" in
    show-user*) printf '%s\n' "$(cat "$SMOKE_STATE/linger" 2>/dev/null || echo no)" ;;
    '--no-ask-password enable-linger '*)
        [[ "${FAIL_LINGER:-0}" == 0 ]] || { echo 'Access denied' >&2; exit 1; }
        [[ "${IGNORE_LINGER:-0}" == 0 ]] || exit 0
        echo yes > "$SMOKE_STATE/linger"
        echo enable-linger >> "$SMOKE_STATE/calls"
        ;;
    *) echo "Unexpected loginctl call: $*" >&2; exit 1 ;;
esac
STUB
cat > "$work/bin/systemctl" <<'STUB'
#!/usr/bin/env bash
set -eu
echo "systemctl $*" >> "$SMOKE_STATE/calls"
case "$*" in
    '--user daemon-reload') ;;
    '--user enable bannershop') touch "$SMOKE_STATE/enabled" ;;
    '--user enable --now bannershop') touch "$SMOKE_STATE/enabled" "$SMOKE_STATE/active" ;;
    '--user restart bannershop') touch "$SMOKE_STATE/active" ;;
    '--user is-active --quiet bannershop') test -f "$SMOKE_STATE/active" ;;
    '--user is-enabled bannershop') test -f "$SMOKE_STATE/enabled" && echo enabled ;;
    '--user status bannershop --no-pager') test -f "$SMOKE_STATE/active" && echo active ;;
    *) echo "Unexpected systemctl call: $*" >&2; exit 1 ;;
esac
STUB
for command in dotnet claude sleep; do
    printf '#!/bin/sh\nexit 0\n' > "$work/bin/$command"
done
chmod +x "$work/bin/"*
export PATH="$work/bin:$PATH" HOME="$work/home" SMOKE_STATE="$work/state"
mkdir -p "$SMOKE_STATE"
run_make() { make --no-print-directory -C "$work/repo" "$@"; }
# Model an unattended boot: an enabled user unit needs a lingering user manager.
unattended_boot() {
    rm -f "$SMOKE_STATE/active"
    if [[ -f "$SMOKE_STATE/enabled" && "$(cat "$SMOKE_STATE/linger" 2>/dev/null || echo no)" == yes ]]; then
        touch "$SMOKE_STATE/active"
    fi
    test -f "$SMOKE_STATE/active" || {
        echo 'FAIL: enabled service stays down on unattended boot (Linger is not yes)' >&2
        return 1
    }
}
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
unattended_boot
echo 'PASS: new installation enables the user manager and service for unattended boot'
# Installed copies must be reused even when npm cannot download anything.
FAIL_NPM=1 run_make -j4 up > "$work/repeat.log" 2>&1
test "$(grep -c 'Checking/installing Codex' "$work/repeat.log")" = 1
grep -q 'BannerShop is up' "$work/repeat.log"
test "$(grep -c '^enable-linger$' "$SMOKE_STATE/calls")" = 1
unattended_boot
echo 'PASS: parallel repeat checks once and reuses installed versions without npm'
# Recover an older installation without invoking build/config/CLI dependencies.
rm -f "$SMOKE_STATE/linger" "$SMOKE_STATE/enabled" "$SMOKE_STATE/active"
run_make status > "$work/status-before.log" 2>&1
grep -q 'WARNING: boot startup is not configured' "$work/status-before.log"
run_make enable-boot NPM= DOTNET= DOCKER= CLAUDE= > "$work/recover.log" 2>&1
test -f "$SMOKE_STATE/active"
if grep -Eq 'Checking/installing|Building|Publishing' "$work/recover.log"; then
    echo 'Recovery unexpectedly invoked build dependencies' >&2; exit 1
fi
unattended_boot
run_make status > "$work/status-after.log" 2>&1
grep -q 'Linger=yes' "$work/status-after.log"
grep -qx enabled "$work/status-after.log"
echo 'PASS: existing installation recovers without rebuild and status reports boot readiness'
# Permissions must fail clearly rather than falsely announcing reboot readiness.
rm -f "$SMOKE_STATE/linger"
cp "$SMOKE_STATE/calls" "$work/calls-before-denial"
if FAIL_LINGER=1 run_make enable-boot > "$work/denied.log" 2>&1; then
    echo 'Expected lingering permission failure' >&2; exit 1
fi
grep -q "loginctl enable-linger $(id -un)" "$work/denied.log"
cmp "$SMOKE_STATE/calls" "$work/calls-before-denial"
if FAIL_LINGER=1 run_make up > "$work/up-denied.log" 2>&1; then
    echo 'Expected installation to stop when boot setup is denied' >&2; exit 1
fi
if grep -q 'BannerShop is up' "$work/up-denied.log"; then
    echo 'Installation claimed success despite denied boot setup' >&2; exit 1
fi
echo 'PASS: denied permission gives an administrator command and prevents false success'
if IGNORE_LINGER=1 run_make enable-boot > "$work/ignored.log" 2>&1; then
    echo 'Expected failure when lingering does not take effect' >&2; exit 1
fi
grep -q 'Linger is not yes' "$work/ignored.log"
echo 'PASS: lingering is verified after enabling'
# An administrator can pre-enable lingering even if the app user cannot change it.
echo yes > "$SMOKE_STATE/linger"
FAIL_LINGER=1 run_make enable-boot > "$work/pre-enabled.log" 2>&1
unattended_boot
echo 'PASS: already-enabled lingering requires no permission to change host settings'
# A real installer error must remain visible and prevent successful startup.
if FAIL_NPM=1 run_make up IMAGE_CLI_PREFIX="$work/fail-cli" > "$work/fail.log" 2>&1; then
    echo 'Expected installation failure' >&2; exit 1
fi
grep -q 'Checking/installing Codex' "$work/fail.log"
grep -q 'ERROR: Could not install codex' "$work/fail.log"
if grep -q 'BannerShop is up' "$work/fail.log"; then
    echo 'Installation claimed success despite failed CLI setup' >&2; exit 1
fi
echo 'PASS: installation failure is logged and stops startup'
