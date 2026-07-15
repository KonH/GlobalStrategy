# Ralph loop - thin wrapper that passes execution to scripts/ralph.py.
#
# See scripts/ralph.py for the full flow description.
#
# Usage (from project root):
#   .\scripts\ralph.ps1 -SpecIndex 45 -MaxIterations 10
#   .\scripts\ralph.ps1 -SpecIndex 45 -SkipCreatePrd          # reuse the existing .ralph/prd.md
#   .\scripts\ralph.ps1 -SpecIndex 45 -SkipPullRequest        # stop after the loop, no commit/PR phase
#   .\scripts\ralph.ps1 -SpecIndex 45 -DangerouslySkipPermissions
#
# Metrics per phase/iteration are appended to .ralph\metrics_<SpecIndex>.csv (gitignored).

param(
    [Parameter(Mandatory = $true)][int]$SpecIndex,
    [int]$MaxIterations = 10,
    [int]$StallLimit = 3,
    [switch]$SkipCreatePrd,
    [switch]$SkipPullRequest,
    [switch]$DangerouslySkipPermissions
)

$ErrorActionPreference = "Stop"

$pythonExe = if (Test-Path ".venv\Scripts\python.exe") { ".venv\Scripts\python.exe" } else { "python" }
$scriptPath = Join-Path $PSScriptRoot "ralph.py"

$pyArgs = @(
    $scriptPath,
    "--spec-index", $SpecIndex,
    "--max-iterations", $MaxIterations,
    "--stall-limit", $StallLimit
)
if ($SkipCreatePrd) { $pyArgs += "--skip-create-prd" }
if ($SkipPullRequest) { $pyArgs += "--skip-pull-request" }
if ($DangerouslySkipPermissions) { $pyArgs += "--dangerously-skip-permissions" }

& $pythonExe @pyArgs
exit $LASTEXITCODE
