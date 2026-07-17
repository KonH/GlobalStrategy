# Ralph loop - thin wrapper that passes execution to scripts/ralph.py.
#
# See scripts/ralph.py for the full flow description.
#
# Usage (from project root):
#   .\scripts\ralph.ps1 -SpecIndex 45 -MaxIterations 10
#   .\scripts\ralph.ps1 -SpecIndex 45 -SkipCreatePrd          # reuse the existing .ralph/prd.md
#   .\scripts\ralph.ps1 -SpecIndex 45 -SkipPullRequest        # stop after the loop, no commit/PR phase
#   .\scripts\ralph.ps1 -SpecIndex 45 -DangerouslySkipPermissions
#   .\scripts\ralph.ps1 -BotFeature opinionTargeting          # bot-feature mode: PRD written by /implement-bot-feature
#
# Exactly one of -SpecIndex / -BotFeature must be given.
#
# Metrics per phase/iteration are appended to .ralph\metrics_<SpecIndex>.csv (spec mode) or
# .ralph\metrics_bot_<BotFeature>.csv (bot mode) — both gitignored.

param(
    [int]$SpecIndex,
    [string]$BotFeature,
    [int]$MaxIterations = 10,
    [int]$StallLimit = 3,
    [switch]$SkipCreatePrd,
    [switch]$SkipPullRequest,
    [switch]$DangerouslySkipPermissions
)

$ErrorActionPreference = "Stop"

if (-not $SpecIndex -and -not $BotFeature) {
    throw "Exactly one of -SpecIndex or -BotFeature must be given."
}
if ($SpecIndex -and $BotFeature) {
    throw "Exactly one of -SpecIndex or -BotFeature must be given."
}

$pythonExe = if (Test-Path ".venv\Scripts\python.exe") { ".venv\Scripts\python.exe" } else { "python" }
$scriptPath = Join-Path $PSScriptRoot "ralph.py"

$pyArgs = @(
    $scriptPath,
    "--max-iterations", $MaxIterations,
    "--stall-limit", $StallLimit
)
if ($SpecIndex) { $pyArgs += @("--spec-index", $SpecIndex) }
if ($BotFeature) { $pyArgs += @("--bot-feature", $BotFeature) }
if ($SkipCreatePrd) { $pyArgs += "--skip-create-prd" }
if ($SkipPullRequest) { $pyArgs += "--skip-pull-request" }
if ($DangerouslySkipPermissions) { $pyArgs += "--dangerously-skip-permissions" }

& $pythonExe @pyArgs
exit $LASTEXITCODE
