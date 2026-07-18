#!/usr/bin/env bash
# Handle Feature Issues - thin wrapper that passes execution to scripts/handle_feature_issues.py.
#
# Run this on a cron schedule in your own environment (NOT this repo's main working copy -
# it does `git reset --hard origin/main`, so point it at a separate dedicated clone).
# See scripts/handle_feature_issues.py and .claude/rules/github_issue_automation.md.
#
# Usage (from the dedicated clone's root):
#   ./scripts/handle_feature_issues.sh
#   ./scripts/handle_feature_issues.sh --max-turns 60
#
# Example crontab entry (hourly):
#   0 * * * * cd /path/to/dedicated-clone && ./scripts/handle_feature_issues.sh >> ~/.local/state/handle_feature_issues.log 2>&1

set -e

PYTHON_BIN="python3"
if ! command -v "$PYTHON_BIN" >/dev/null 2>&1; then
    PYTHON_BIN="python"
fi

exec "$PYTHON_BIN" "$(dirname "$0")/handle_feature_issues.py" "$@"
