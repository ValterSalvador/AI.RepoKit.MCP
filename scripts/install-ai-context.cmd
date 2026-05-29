@echo off
setlocal
cd /d "%~dp0"
airepo.exe bootstrap --repo "%CD%" --clients codex,vscode,vs --mcp --apply --backup
echo.
pause
