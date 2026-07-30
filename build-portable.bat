@echo off
setlocal

set "PROJECT_DIR=%~dp0"
cd /d "%PROJECT_DIR%"

echo Stopping existing MCPHub frontend instances...
taskkill /f /im "MCPHub-Frontend.exe" >nul 2>nul
taskkill /f /im "MCPHub-Frontend-split-view.exe" >nul 2>nul

where pnpm >nul 2>nul
if errorlevel 1 (
  echo ERROR: pnpm was not found on PATH.
  echo Install Node.js, then run: corepack enable
  pause
  exit /b 1
)

where cargo >nul 2>nul
if errorlevel 1 (
  echo ERROR: Rust/Cargo was not found on PATH.
  echo Install the official Rust MSVC toolchain from https://rustup.rs/
  pause
  exit /b 1
)

echo Preparing Chromium extensions for the YouTube webview...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_DIR%\install-browser-extensions.ps1"
if errorlevel 1 goto :failed

echo Installing frontend dependencies...
call pnpm install --no-frozen-lockfile
if errorlevel 1 goto :failed

echo Building the portable Windows executable...
call pnpm tauri:build
if errorlevel 1 goto :failed

set "DESKTOP=%USERPROFILE%\Desktop"
copy /y "src-tauri\target\release\MCPHub-Frontend.exe" "%DESKTOP%\MCPHub-Frontend.exe" >nul
if errorlevel 1 goto :failed

set "PORTABLE_EXTENSIONS=%DESKTOP%\MCPHub-Frontend-data\extensions"
if exist "%PORTABLE_EXTENSIONS%" rmdir /s /q "%PORTABLE_EXTENSIONS%"
xcopy /e /i /y "src-tauri\extensions" "%PORTABLE_EXTENSIONS%" >nul
if errorlevel 1 goto :failed

echo.
echo Portable executable on your Desktop:
echo %DESKTOP%\MCPHub-Frontend.exe

echo Starting the portable application...
start "" "%DESKTOP%\MCPHub-Frontend.exe"
if errorlevel 1 goto :failed

exit /b 0

:failed
echo.
echo Build failed. Review the output above.
pause
exit /b 1
