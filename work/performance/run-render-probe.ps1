$ErrorActionPreference = 'Stop'
# The v0.1.3 tag retains the original comparison. Current behavior is checked below.
& (Join-Path (Split-Path $PSScriptRoot -Parent) 'browse\run-checks.ps1')
