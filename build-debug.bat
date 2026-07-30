@echo off
setlocal

set "PROJECT_DIR=%~dp0"
cd /d "%PROJECT_DIR%"

where pnpm >nul 2>nul
if errorlevel 1 (
  echo ERROR: pnpm was not found on PATH.
  echo Install Node.js 20 or newer, then run: corepack enable
  exit /b 1
)

call pnpm install --no-frozen-lockfile
if errorlevel 1 goto :failed

echo Building the unpacked Electron application...
call pnpm electron:build:dir
if errorlevel 1 goto :failed

echo.
echo Unpacked executable:
echo %PROJECT_DIR%dist-electron\win-unpacked\MCPHub Frontend.exe
exit /b 0

:failed
echo.
echo Debug build failed. Review the output above.
exit /b 1
