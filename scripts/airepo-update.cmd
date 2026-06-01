@echo off
setlocal

set "SCRIPT_DIR=%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%airepo-update.ps1" %*

if errorlevel 1 (
  echo.
  echo [FAIL] AI RepoKit update failed.
  pause
  exit /b 1
)

exit /b 0