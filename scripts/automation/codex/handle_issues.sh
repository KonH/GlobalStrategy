#!/usr/bin/env bash
# Handle Labeled Issues (Codex) - thin wrapper that passes execution to handle_issues.py in this same folder.
#
# Run this on a cron schedule from a dedicated automation checkout. It resets to origin/main
# before invoking Codex, so do not use a development checkout or create a worktree.
# See handle_issues.py and .codex/skills/codex-issue/SKILL.md.
#
# Only issues/PRs labeled 'codex' are ever considered - create the labels once per repo:
#   gh label create codex --color 5319E7 --description "Execute this item's prompt via the Codex automation"
#   gh label create codex-in-progress --color FBCA04 --description "Automation actively working this item"
#   gh label create codex-needs-attention --color D93F0B --description "Automation waiting on the owner"
#   gh label create codex-complete --color 0E8A16 --description "Automation finished this item's prompt"
#
# The labels are the whole state machine: an item is picked up iff it carries 'codex' and
# none of the three status labels. Resume a needs-attention/complete item by replying in a
# comment and removing that status label. Codex is only invoked when discovery finds at
# least one candidate.
#
# The Python script writes its own auto-rotating log (Logs/handle_issues_codex.log,
# 5MB x 5 backups by default) - don't also pipe stdout to a separate `>> file.log`, that
# would just grow unbounded next to it with no rotation. Redirect to /dev/null instead so
# cron doesn't try to mail you the output:
#
# Example crontab entry (every 15 minutes):
#   */15 * * * * cd /path/to/dedicated-clone && ./scripts/automation/codex/handle_issues.sh >/dev/null 2>&1
#
# Usage (from the dedicated clone's root):
#   ./scripts/automation/codex/handle_issues.sh
#   ./scripts/automation/codex/handle_issues.sh --model gpt-5.6-sol --effort high

set -e

PYTHON_BIN="python3"
if ! command -v "$PYTHON_BIN" >/dev/null 2>&1; then
    PYTHON_BIN="python"
fi

exec "$PYTHON_BIN" "$(dirname "$0")/handle_issues.py" "$@"
