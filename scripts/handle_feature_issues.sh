#!/usr/bin/env bash
# Handle Feature Issues - thin wrapper that passes execution to scripts/handle_feature_issues.py.
#
# Run this on a cron schedule in your own environment (NOT this repo's main working copy -
# it does `git reset --hard origin/main`, so point it at a separate dedicated clone).
# See scripts/handle_feature_issues.py and .claude/rules/github_issue_automation.md.
#
# Only issues/PRs labeled 'claude' are ever considered - create the label once per repo:
#   gh label create claude --color 5319E7 --description "Feature-issue automation"
#
# Usage (from the dedicated clone's root):
#   ./scripts/handle_feature_issues.sh
#   ./scripts/handle_feature_issues.sh --since-hours 2 --max-turns 60
#
# --since-hours (default 1) should match the cron interval below - it's the lookback
# window used to decide whether there's anything new to act on at all. claude -p is only
# invoked (and only then spends subscription usage) when that check finds something.
#
# Example crontab entry (hourly):
#   0 * * * * cd /path/to/dedicated-clone && ./scripts/handle_feature_issues.sh >> ~/.local/state/handle_feature_issues.log 2>&1

set -e

PYTHON_BIN="python3"
if ! command -v "$PYTHON_BIN" >/dev/null 2>&1; then
    PYTHON_BIN="python"
fi

exec "$PYTHON_BIN" "$(dirname "$0")/handle_feature_issues.py" "$@"
