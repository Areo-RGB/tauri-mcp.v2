@echo off
setlocal

set "PROJECT_DIR=%~dp0"
cd /d "%PROJECT_DIR%"

where pnpm >nul 2>nul
if errorlevel 1 (
  echo ERROR: pnpm was not found on PATH.
  echo Install Node.js, then run: corepack enable
  exit /b 1
)

where cargo >nul 2>nul
if errorlevel 1 (
  echo ERROR: Rust/Cargo was not found on PATH.
  echo Install the official Rust MSVC toolchain from https://rustup.rs/
  exit /b 1
)

echo Building MCPHub in the faster debug profile (no release optimization, no installer)...
call pnpm tauri:build:debug
if errorlevel 1 goto :failed

echo.
echo Debug executable:
echo %PROJECT_DIR%src-tauri\target\debug\MCPHub-Frontend.exe
exit /b 0

:failed
echo.
echo Debug build failed. Review the output above.
exit /b 1
