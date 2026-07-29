# Run from a dedicated automation clone, not the primary working copy.
# Create the `cursor`, `cursor-in-progress`, `cursor-needs-attention`, and `cursor-complete`
# labels in KonH/GlobalStrategy before scheduling this script.
param([string]$Model = "cursor-grok-4.5-high")

$ErrorActionPreference = "Stop"
$pythonExe = if (Test-Path ".venv\Scripts\python.exe") { ".venv\Scripts\python.exe" } else { "python" }
& $pythonExe (Join-Path $PSScriptRoot "handle_issues.py") --model $Model
exit $LASTEXITCODE
