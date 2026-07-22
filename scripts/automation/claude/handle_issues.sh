#!/usr/bin/env bash
# Handle Feature Issues (Claude) - thin wrapper that passes execution to handle_issues.py in this same folder.
#
# Run this on a cron schedule in your own environment (NOT this repo's main working copy -
# it does `git reset --hard origin/main`, so point it at a separate dedicated clone).
# See handle_issues.py and .claude/rules/github_issue_automation.md.
#
# Only issues labeled 'claude' are ever considered - create the labels once per repo:
#   gh label create claude --color 5319E7 --description "Feature-issue automation"
#   gh label create claude-in-progress --color FBCA04 --description "Automation actively working this issue"
#   gh label create claude-needs-attention --color D93F0B --description "Automation stopped, needs a human"
#   gh label create code-only --color 0E8A16 --description "Implementable without Unity Editor/MCP or image generation"
#   gh label create full-env-required --color 5319E7 --description "Needs Unity Editor/MCP or image generation to implement"
#
# Usage (from the dedicated clone's root):
#   ./scripts/automation/claude/handle_issues.sh
#   ./scripts/automation/claude/handle_issues.sh --since-hours 2 --max-turns 60
#   ./scripts/automation/claude/handle_issues.sh --since-minutes 15
#
# --since-hours/--since-minutes (combined; default 1h if both omitted) should match the
# cron interval below - it's the lookback window used to decide whether there's anything
# new to act on at all. claude -p is only invoked (and only then spends subscription
# usage) when that check finds something.
#
# The Python script writes its own auto-rotating log (Logs/handle_feature_issues.log,
# 5MB x 5 backups by default) - don't also pipe stdout to a separate `>> file.log`, that
# would just grow unbounded next to it with no rotation. Redirect to /dev/null instead so
# cron doesn't try to mail you the output:
#
# Example crontab entry (hourly):
#   0 * * * * cd /path/to/dedicated-clone && ./scripts/automation/claude/handle_issues.sh >/dev/null 2>&1
#
# Example crontab entry (every 15 minutes):
#   */15 * * * * cd /path/to/dedicated-clone && ./scripts/automation/claude/handle_issues.sh --since-minutes 15 >/dev/null 2>&1

set -e

PYTHON_BIN="python3"
if ! command -v "$PYTHON_BIN" >/dev/null 2>&1; then
    PYTHON_BIN="python"
fi

exec "$PYTHON_BIN" "$(dirname "$0")/handle_issues.py" "$@"
