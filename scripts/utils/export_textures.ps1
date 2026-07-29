# Export TextureSources -> Assets/Textures (resize + transparent pad).
#
# Usage (from project root):
#   .\scripts\utils\export_textures.ps1
#   .\scripts\utils\export_textures.ps1 --dry-run
#   .\scripts\utils\export_textures.ps1 --only Flags/Countries
#   .\scripts\utils\export_textures.ps1 --only Icons
#   .\scripts\utils\export_textures.ps1 -v --dry-run --only Icons/ResourceRow

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scriptPath = Join-Path $PSScriptRoot "export_textures.py"
$venvPython = Join-Path $repoRoot ".venv\Scripts\python.exe"
$pythonExe = if (Test-Path $venvPython) { $venvPython } else { "python" }

Push-Location $repoRoot
try {
    & $pythonExe $scriptPath @args
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
