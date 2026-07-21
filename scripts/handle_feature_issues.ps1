# Handle Feature Issues - thin wrapper that passes execution to scripts/handle_feature_issues.py.
#
# Run this on a scheduled task in your own environment (NOT this repo's main working copy -
# it does `git reset --hard origin/main`, so point it at a separate dedicated clone).
# See scripts/handle_feature_issues.py and .claude/rules/github_issue_automation.md.
#
# Only issues labeled 'claude' are ever considered - create the labels once per repo:
#   gh label create claude --color 5319E7 --description "Feature-issue automation"
#   gh label create claude-in-progress --color FBCA04 --description "Automation actively working this issue"
#   gh label create claude-needs-attention --color D93F0B --description "Automation stopped, needs a human"
#
# Note: flock-based process locking (see handle_feature_issues.py) is POSIX-only and is a
# no-op on Windows - use Task Scheduler's own "don't start a new instance if already
# running" setting for the same guarantee here.
#
# Usage (from the dedicated clone's root):
#   .\scripts\handle_feature_issues.ps1
#   .\scripts\handle_feature_issues.ps1 -SinceHours 2 -MaxTurns 60
#   .\scripts\handle_feature_issues.ps1 -SinceMinutes 15
#
# -SinceHours/-SinceMinutes (combined; default 1h if both omitted) should match the Task
# Scheduler interval below - it's the lookback window used to decide whether there's
# anything new to act on at all. claude -p is only invoked (and only then spends
# subscription usage) when that check finds something.
#
# The Python script writes its own auto-rotating log (Logs\handle_feature_issues.log,
# 5MB x 5 backups by default) - no need to also capture Task Scheduler's own output.
#
# Example Task Scheduler action (hourly, or every 15 min with -SinceMinutes 15 passed as
# an argument): run this script with working directory set to the dedicated clone's root.

param(
    [int]$MaxTurns = 40,
    [double]$SinceHours = 0,
    [double]$SinceMinutes = 0
)

$ErrorActionPreference = "Stop"

$pythonExe = if (Test-Path ".venv\Scripts\python.exe") { ".venv\Scripts\python.exe" } else { "python" }
$scriptPath = Join-Path $PSScriptRoot "handle_feature_issues.py"

& $pythonExe $scriptPath --max-turns $MaxTurns --since-hours $SinceHours --since-minutes $SinceMinutes
exit $LASTEXITCODE
