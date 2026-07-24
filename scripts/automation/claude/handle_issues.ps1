# Handle Labeled Issues (Claude) - thin wrapper that passes execution to handle_issues.py in this same folder.
#
# Run this on a scheduled task in your own environment (NOT this repo's main working copy -
# it force-resets the checkout to each candidate's branch and removes untracked files, so
# point it at a separate dedicated clone).
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
# Note: the process lock (see handle_issues.py) is cross-platform (msvcrt on Windows, fcntl on
# POSIX) - also set Task Scheduler's own "don't start a new instance if already running" as a
# second safeguard.
#
# The Python script writes its own auto-rotating log (Logs\handle_issues_claude.log,
# 5MB x 5 backups by default) - no need to also capture Task Scheduler's own output.
#
# Example Task Scheduler action (e.g. every 15 min): run this script with working directory
# set to the dedicated clone's root.
#
# Usage (from the dedicated clone's root):
#   .\scripts\automation\claude\handle_issues.ps1
#   .\scripts\automation\claude\handle_issues.ps1 -MaxTurns 60

param(
    [int]$MaxTurns = 40
)

$ErrorActionPreference = "Stop"

$pythonExe = if (Test-Path ".venv\Scripts\python.exe") { ".venv\Scripts\python.exe" } else { "python" }
$scriptPath = Join-Path $PSScriptRoot "handle_issues.py"

& $pythonExe $scriptPath --max-turns $MaxTurns
exit $LASTEXITCODE
