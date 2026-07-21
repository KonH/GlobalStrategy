# Handle Codex Feature Issues - thin wrapper for scripts/handle_codex_feature_issues.py.
#
# Run this scheduled task from this dedicated automation checkout. It resets to origin/main
# before each poll, so do not use a development checkout or create a worktree.
# See scripts/handle_codex_feature_issues.py and .codex/skills/codex-feature-issue/SKILL.md.
#
# Only issues labeled 'codex' are ever considered - create the labels once per repo:
#   gh label create codex --color 5319E7 --description "Codex feature-issue automation"
#   gh label create codex-in-progress --color FBCA04 --description "Automation actively working this issue"
#   gh label create codex-needs-attention --color D93F0B --description "Automation stopped, needs a human"
#   gh label create code-only --color 0E8A16 --description "Implementable without Unity Editor/MCP or image generation"
#   gh label create full-env-required --color 5319E7 --description "Needs Unity Editor/MCP or image generation to implement"
#
# The Python runner uses an OS-level non-blocking lock on Windows and POSIX. Also set Task
# Scheduler's "don't start a new instance if already running" option as a second safeguard.
#
# Usage (from the dedicated clone's root):
#   .\scripts\handle_codex_feature_issues.ps1
#   .\scripts\handle_codex_feature_issues.ps1 -SinceHours 2
#   .\scripts\handle_codex_feature_issues.ps1 -SinceMinutes 15 -Model gpt-5.6-sol -Effort high
#   .\scripts\handle_codex_feature_issues.ps1 -Sandbox danger-full-access
#
# -SinceHours/-SinceMinutes (combined; default 1h if both omitted) should match the Task
# Scheduler interval below - it's the lookback window used to decide whether there's
# anything new to act on at all. Codex is invoked only when that check finds something.
#
# The Python script writes its own auto-rotating log (Logs\handle_codex_feature_issues.log,
# 5MB x 5 backups by default) - no need to also capture Task Scheduler's own output.
#
# Example Task Scheduler action (hourly, or every 15 min with -SinceMinutes 15 passed as
# an argument): run this script with working directory set to the dedicated clone's root.

param(
    [double]$SinceHours = 0,
    [double]$SinceMinutes = 0,
    [string]$Model = "gpt-5.6-sol",
    [ValidateSet("minimal", "low", "medium", "high", "xhigh")]
    [string]$Effort = "high",
    [ValidateSet("read-only", "workspace-write", "danger-full-access")]
    [string]$Sandbox = "workspace-write",
    [switch]$DangerouslySkipPermissions
)

$ErrorActionPreference = "Stop"

$pythonExe = if (Test-Path ".venv\Scripts\python.exe") { ".venv\Scripts\python.exe" } else { "python" }
$scriptPath = Join-Path $PSScriptRoot "handle_codex_feature_issues.py"

$pyArgs = @($scriptPath, "--since-hours", $SinceHours, "--since-minutes", $SinceMinutes, "--model", $Model, "--effort", $Effort, "--sandbox", $Sandbox)
if ($DangerouslySkipPermissions) { $pyArgs += "--dangerously-skip-permissions" }
& $pythonExe @pyArgs
exit $LASTEXITCODE
