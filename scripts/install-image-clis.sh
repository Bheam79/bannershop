#!/usr/bin/env bash
# Install only BannerShop's own CLI copies. Authentication stays in /admin/settings.
set -euo pipefail

prefix=${1:?npm installation prefix required}
codex_version=${2:?Codex version required}
grok_version=${3:?Grok version required}
npm=${NPM:-npm}
node=${NODE:-node}

command -v "$npm" >/dev/null || { echo "ERROR: npm is required to install image CLIs" >&2; exit 1; }
command -v "$node" >/dev/null || { echo "ERROR: Node.js 20+ is required to install image CLIs" >&2; exit 1; }
"$node" -e 'if (Number(process.versions.node.split(".")[0]) < 20) { console.error("ERROR: Node.js 20+ is required for Grok CLI"); process.exit(1); }'
# npm and the CLI shims use /usr/bin/env node, including with nvm installations.
export PATH="$(dirname "$(command -v "$node")"):$PATH"

install_cli() {
    local package=$1 version=$2 executable=$3 installed=''
    local manifest="$prefix/lib/node_modules/$package/package.json"
    if [[ -f "$manifest" ]]; then
        installed=$("$node" -e 'console.log(JSON.parse(require("fs").readFileSync(process.argv[1], "utf8")).version)' "$manifest")
    fi
    if [[ "$installed" != "$version" || ! -x "$prefix/bin/$executable" ]]; then
        echo ">>> Installing $package@$version under $prefix..."
        # Grok's postinstall writes a binary and config under GROK_HOME, even
        # with --prefix. Keep those writes away from the operator's ~/.grok.
        GROK_HOME="$prefix/grok-install" "$npm" install --global --prefix "$prefix" --include=optional --engine-strict \
            --no-audit --no-fund "$package@$version" || {
            echo "ERROR: Could not install $executable; check npm registry access and permissions for $prefix" >&2
            exit 1
        }
    else
        echo ">>> Keeping $executable $version (already installed; no npm download needed)."
    fi
    echo ">>> Verifying $prefix/bin/$executable"
    "$prefix/bin/$executable" --version || {
        echo "ERROR: $prefix/bin/$executable cannot run on this host" >&2
        exit 1
    }
}

install_cli @openai/codex "$codex_version" codex
install_cli @xai-official/grok "$grok_version" grok
echo '>>> Image CLIs ready. Connect both accounts under /admin/settings.'
