# Ralph loop (Codex) - thin wrapper that passes execution to ralph.py in this same folder.
#
# See ralph.py for the full flow description.
#
# Usage (from project root):
#   .\scripts\automation\codex\ralph.ps1 -Spec 26_07_11_10_province-ownership -MaxIterations 10
#   .\scripts\automation\codex\ralph.ps1 -Spec 26_07_11_10_province-ownership -SkipCreatePrd          # reuse the existing .ralph/prd.md
#   .\scripts\automation\codex\ralph.ps1 -Spec 26_07_11_10_province-ownership -SkipPullRequest        # stop after the loop, no commit/PR phase
#   .\scripts\automation\codex\ralph.ps1 -Spec 26_07_11_10_province-ownership -DangerouslySkipPermissions
#   .\scripts\automation\codex\ralph.ps1 -BotFeature opinionTargeting          # bot-feature mode: PRD written by /implement-bot-feature
#   .\scripts\automation\codex\ralph.ps1 -PerfTarget CountryPopulationCollector # perf mode: PRD written by /optimize-performance
#
# Exactly one of -Spec / -BotFeature / -PerfTarget must be given.
#
# Metrics per phase/iteration are appended to .ralph\metrics_<SpecId>.csv (spec mode),
# .ralph\metrics_bot_<BotFeature>.csv (bot mode), or .ralph\metrics_perf_<PerfTarget>.csv
# (perf mode) — all gitignored.

param(
    [string]$Spec,
    [string]$BotFeature,
    [string]$PerfTarget,
    [int]$MaxIterations = 10,
    [int]$StallLimit = 3,
    [switch]$SkipCreatePrd,
    [switch]$SkipPullRequest,
    [switch]$DangerouslySkipPermissions,
    [string]$Model,
    [ValidateSet("minimal", "low", "medium", "high", "xhigh")]
    [string]$Effort,
    [ValidateSet("code-only", "full-env-headless")]
    [string]$Env,
    [switch]$AutoAdjustIterations,
    [ValidateSet("read-only", "workspace-write", "danger-full-access")]
    [string]$Sandbox
)

$ErrorActionPreference = "Stop"

$modeCount = @($Spec, $BotFeature, $PerfTarget) | Where-Object { $_ } | Measure-Object | Select-Object -ExpandProperty Count
if ($modeCount -ne 1) {
    throw "Exactly one of -Spec, -BotFeature, or -PerfTarget must be given."
}

$pythonExe = if (Test-Path ".venv\Scripts\python.exe") { ".venv\Scripts\python.exe" } else { "python" }
$scriptPath = Join-Path $PSScriptRoot "ralph.py"

$pyArgs = @(
    $scriptPath,
    "--max-iterations", $MaxIterations,
    "--stall-limit", $StallLimit
)
if ($Spec) { $pyArgs += @("--spec", $Spec) }
if ($BotFeature) { $pyArgs += @("--bot-feature", $BotFeature) }
if ($PerfTarget) { $pyArgs += @("--perf-target", $PerfTarget) }
if ($SkipCreatePrd) { $pyArgs += "--skip-create-prd" }
if ($SkipPullRequest) { $pyArgs += "--skip-pull-request" }
if ($DangerouslySkipPermissions) { $pyArgs += "--dangerously-skip-permissions" }
if ($Model) { $pyArgs += @("--model", $Model) }
if ($Effort) { $pyArgs += @("--effort", $Effort) }
if ($Env) { $pyArgs += @("--env", $Env) }
if ($AutoAdjustIterations) { $pyArgs += "--auto-adjust-iterations" }
if ($Sandbox) { $pyArgs += @("--sandbox", $Sandbox) }

& $pythonExe @pyArgs
exit $LASTEXITCODE
