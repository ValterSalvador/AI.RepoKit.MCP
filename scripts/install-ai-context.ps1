$ErrorActionPreference = "Stop"

Set-Location $PSScriptRoot
& ".\airepo.exe" bootstrap --repo (Get-Location).Path --clients codex,vscode,vs --mcp --apply --backup
Write-Host ""
Read-Host "Press Enter to close"
