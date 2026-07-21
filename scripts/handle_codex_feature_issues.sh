#!/usr/bin/env bash
# Handle Codex Feature Issues - thin wrapper that passes execution to scripts/handle_codex_feature_issues.py.
#
# Run this on a cron schedule in this dedicated automation checkout. It resets to origin/main
# before each poll, so do not use a development checkout.
# See scripts/handle_codex_feature_issues.py and .codex/skills/codex-feature-issue/SKILL.md.
#
# Only issues labeled 'codex' are ever considered - create the labels once per repo:
#   gh label create codex --color 5319E7 --description "Codex feature-issue automation"
#   gh label create codex-in-progress --color FBCA04 --description "Automation actively working this issue"
#   gh label create codex-needs-attention --color D93F0B --description "Automation stopped, needs a human"
#   gh label create code-only --color 0E8A16 --description "Implementable without Unity Editor/MCP or image generation"
#   gh label create full-env-required --color 5319E7 --description "Needs Unity Editor/MCP or image generation to implement"
#
# Usage (from the dedicated clone's root):
#   ./scripts/handle_codex_feature_issues.sh
#   ./scripts/handle_codex_feature_issues.sh --since-hours 2
#   ./scripts/handle_codex_feature_issues.sh --since-minutes 15 --model gpt-5.6-sol --effort high
#
# --since-hours/--since-minutes (combined; default 1h if both omitted) should match the
# cron interval below - it's the lookback window used to decide whether there's anything
# new to act on at all. Codex is invoked only when that check finds something.
#
# The Python script writes its own auto-rotating log (Logs/handle_codex_feature_issues.log,
# 5MB x 5 backups by default) - don't also pipe stdout to a separate `>> file.log`, that
# would just grow unbounded next to it with no rotation. Redirect to /dev/null instead so
# cron doesn't try to mail you the output:
#
# Example crontab entry (hourly):
#   0 * * * * cd /path/to/dedicated-clone && ./scripts/handle_codex_feature_issues.sh >/dev/null 2>&1
#
# Example crontab entry (every 15 minutes):
#   */15 * * * * cd /path/to/dedicated-clone && ./scripts/handle_codex_feature_issues.sh --since-minutes 15 >/dev/null 2>&1

set -e

PYTHON_BIN="python3"
if ! command -v "$PYTHON_BIN" >/dev/null 2>&1; then
    PYTHON_BIN="python"
fi

exec "$PYTHON_BIN" "$(dirname "$0")/handle_codex_feature_issues.py" "$@"
