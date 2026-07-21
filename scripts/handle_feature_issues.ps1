# Handle Feature Issues - thin wrapper that passes execution to scripts/handle_feature_issues.py.
#
# Run this on a scheduled task in your own environment (NOT this repo's main working copy -
# it does `git reset --hard origin/main`, so point it at a separate dedicated clone).
# See scripts/handle_feature_issues.py and .claude/rules/github_issue_automation.md.
#
# Only issues/PRs labeled 'claude' are ever considered - create the label once per repo:
#   gh label create claude --color 5319E7 --description "Feature-issue automation"
#
# Usage (from the dedicated clone's root):
#   .\scripts\handle_feature_issues.ps1
#   .\scripts\handle_feature_issues.ps1 -SinceHours 2 -MaxTurns 60
#
# -SinceHours (default 1) should match the Task Scheduler interval below - it's the
# lookback window used to decide whether there's anything new to act on at all. claude -p
# is only invoked (and only then spends subscription usage) when that check finds something.
#
# Example Task Scheduler action (hourly): run this script with working directory set to
# the dedicated clone's root.

param(
    [int]$MaxTurns = 40,
    [double]$SinceHours = 1
)

$ErrorActionPreference = "Stop"

$pythonExe = if (Test-Path ".venv\Scripts\python.exe") { ".venv\Scripts\python.exe" } else { "python" }
$scriptPath = Join-Path $PSScriptRoot "handle_feature_issues.py"

& $pythonExe $scriptPath --max-turns $MaxTurns --since-hours $SinceHours
exit $LASTEXITCODE
