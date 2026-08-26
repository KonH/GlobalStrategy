$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$venvPython = Join-Path $repoRoot ".venv\Scripts\python.exe"

if (Test-Path $venvPython) {
    & $venvPython (Join-Path $PSScriptRoot "cursor_session_identity.py") @args
} else {
    & python (Join-Path $PSScriptRoot "cursor_session_identity.py") @args
}
exit $LASTEXITCODE
