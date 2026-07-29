#!/usr/bin/env bash
# Run from a dedicated automation clone, not the primary working copy.
set -e
PYTHON_BIN="python3"
if ! command -v "$PYTHON_BIN" >/dev/null 2>&1; then
    PYTHON_BIN="python"
fi
exec "$PYTHON_BIN" "$(dirname "$0")/handle_issues.py" "$@"
