$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$venvPython = Join-Path $repoRoot ".venv\Scripts\python.exe"

if (Test-Path $venvPython) {
    & $venvPython (Join-Path $PSScriptRoot "collect_usage.py") @args
} else {
    & python (Join-Path $PSScriptRoot "collect_usage.py") @args
}
