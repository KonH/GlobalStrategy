#!/usr/bin/env pwsh
& python (Join-Path $PSScriptRoot "handle_issues_auto.py") @args
exit $LASTEXITCODE
