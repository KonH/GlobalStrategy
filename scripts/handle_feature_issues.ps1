# Handle Feature Issues - thin wrapper that passes execution to scripts/handle_feature_issues.py.
#
# Run this on a scheduled task in your own environment (NOT this repo's main working copy -
# it does `git reset --hard origin/main`, so point it at a separate dedicated clone).
# See scripts/handle_feature_issues.py and .claude/rules/github_issue_automation.md.
#
# Usage (from the dedicated clone's root):
#   .\scripts\handle_feature_issues.ps1
#   .\scripts\handle_feature_issues.ps1 -MaxTurns 60
#
# Example Task Scheduler action (hourly): run this script with working directory set to
# the dedicated clone's root.

param(
    [int]$MaxTurns = 40
)

$ErrorActionPreference = "Stop"

$pythonExe = if (Test-Path ".venv\Scripts\python.exe") { ".venv\Scripts\python.exe" } else { "python" }
$scriptPath = Join-Path $PSScriptRoot "handle_feature_issues.py"

& $pythonExe $scriptPath --max-turns $MaxTurns
exit $LASTEXITCODE
