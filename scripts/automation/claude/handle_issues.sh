#!/usr/bin/env bash
# Handle Labeled Issues (Claude) - thin wrapper that passes execution to handle_issues.py in this same folder.
#
# Run this on a cron schedule in your own environment (NOT this repo's main working copy -
# it does `git reset --hard origin/main`, so point it at a separate dedicated clone).
# See handle_issues.py and .claude/skills/github-issue-automation/SKILL.md.
#
# Only issues/PRs labeled 'claude' are ever considered - create the labels once per repo:
#   gh label create claude --color 5319E7 --description "Execute this item's prompt via the Claude automation"
#   gh label create claude-in-progress --color FBCA04 --description "Automation actively working this item"
#   gh label create claude-needs-attention --color D93F0B --description "Automation waiting on the owner"
#   gh label create claude-complete --color 0E8A16 --description "Automation finished this item's prompt"
#
# The labels are the whole state machine: an item is picked up iff it carries 'claude' and
# none of the three status labels. Resume a needs-attention/complete item by replying in a
# comment and removing that status label. claude -p is only invoked (and only then spends
# subscription usage) when discovery finds at least one candidate.
#
# The Python script writes its own auto-rotating log (Logs/handle_issues_claude.log,
# 5MB x 5 backups by default) - don't also pipe stdout to a separate `>> file.log`, that
# would just grow unbounded next to it with no rotation. Redirect to /dev/null instead so
# cron doesn't try to mail you the output:
#
# Example crontab entry (every 15 minutes):
#   */15 * * * * cd /path/to/dedicated-clone && ./scripts/automation/claude/handle_issues.sh >/dev/null 2>&1
#
# Usage (from the dedicated clone's root):
#   ./scripts/automation/claude/handle_issues.sh
#   ./scripts/automation/claude/handle_issues.sh --max-turns 60

set -e

PYTHON_BIN="python3"
if ! command -v "$PYTHON_BIN" >/dev/null 2>&1; then
    PYTHON_BIN="python"
fi

exec "$PYTHON_BIN" "$(dirname "$0")/handle_issues.py" "$@"
