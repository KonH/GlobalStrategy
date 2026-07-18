# Ralph loop - thin wrapper that passes execution to scripts/ralph.py.
#
# See scripts/ralph.py for the full flow description.
#
# Usage (from project root):
#   .\scripts\ralph.ps1 -Spec 26_07_11_10_province-ownership -MaxIterations 10
#   .\scripts\ralph.ps1 -Spec 26_07_11_10_province-ownership -SkipCreatePrd          # reuse the existing .ralph/prd.md
#   .\scripts\ralph.ps1 -Spec 26_07_11_10_province-ownership -SkipPullRequest        # stop after the loop, no commit/PR phase
#   .\scripts\ralph.ps1 -Spec 26_07_11_10_province-ownership -DangerouslySkipPermissions
#   .\scripts\ralph.ps1 -BotFeature opinionTargeting          # bot-feature mode: PRD written by /implement-bot-feature
#
# Exactly one of -Spec / -BotFeature must be given.
#
# Metrics per phase/iteration are appended to .ralph\metrics_<SpecId>.csv (spec mode) or
# .ralph\metrics_bot_<BotFeature>.csv (bot mode) — both gitignored.

param(
    [string]$Spec,
    [string]$BotFeature,
    [int]$MaxIterations = 10,
    [int]$StallLimit = 3,
    [switch]$SkipCreatePrd,
    [switch]$SkipPullRequest,
    [switch]$DangerouslySkipPermissions
)

$ErrorActionPreference = "Stop"

if (-not $Spec -and -not $BotFeature) {
    throw "Exactly one of -Spec or -BotFeature must be given."
}
if ($Spec -and $BotFeature) {
    throw "Exactly one of -Spec or -BotFeature must be given."
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
if ($SkipCreatePrd) { $pyArgs += "--skip-create-prd" }
if ($SkipPullRequest) { $pyArgs += "--skip-pull-request" }
if ($DangerouslySkipPermissions) { $pyArgs += "--dangerously-skip-permissions" }

& $pythonExe @pyArgs
exit $LASTEXITCODE
