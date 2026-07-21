#!/usr/bin/env bash
# Codex Ralph loop - thin wrapper that passes execution to scripts/codex_ralph.py.
#
# Usage (from project root):
#   ./scripts/ralph.sh --spec 26_07_11_10_province-ownership --max-iterations 10
#   ./scripts/ralph.sh --spec 26_07_11_10_province-ownership --skip-create-prd
#   ./scripts/ralph.sh --spec 26_07_11_10_province-ownership --skip-pull-request
#   ./scripts/ralph.sh --spec 26_07_11_10_province-ownership --dangerously-skip-permissions
#
# See scripts/codex_ralph.py for the full flow description.

set -e

PYTHON_BIN="python3"
if ! command -v "$PYTHON_BIN" >/dev/null 2>&1; then
    PYTHON_BIN="python"
fi

exec "$PYTHON_BIN" "$(dirname "$0")/codex_ralph.py" "$@"
