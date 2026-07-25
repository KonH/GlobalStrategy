#!/usr/bin/env bash
# Export TextureSources -> Assets/Textures (resize + transparent pad).
#
# Usage (from project root):
#   ./scripts/utils/export_textures.sh
#   ./scripts/utils/export_textures.sh --dry-run
#   ./scripts/utils/export_textures.sh --only Flags/Countries
#   ./scripts/utils/export_textures.sh --only Icons

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

if [[ -x "$ROOT/.venv/bin/python" ]]; then
    PYTHON_BIN="$ROOT/.venv/bin/python"
elif command -v python3 >/dev/null 2>&1; then
    PYTHON_BIN="python3"
else
    PYTHON_BIN="python"
fi

exec "$PYTHON_BIN" "$ROOT/scripts/utils/export_textures.py" "$@"
