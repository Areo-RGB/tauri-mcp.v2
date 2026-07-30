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

echo Installing frontend dependencies...
call pnpm install --no-frozen-lockfile
if errorlevel 1 goto :failed

echo Building the portable Windows executable...
call pnpm tauri:build
if errorlevel 1 goto :failed

set "DESKTOP=%USERPROFILE%\Desktop"
copy /y "src-tauri\target\release\MCPHub-Frontend.exe" "%DESKTOP%\MCPHub-Frontend.exe" >nul
if errorlevel 1 goto :failed

set "PORTABLE_EXTENSION=%DESKTOP%\MCPHub-Frontend-extension"
if exist "%PORTABLE_EXTENSION%" rmdir /s /q "%PORTABLE_EXTENSION%"
xcopy /e /i /y "src-tauri\extensions\chapter-clipper" "%PORTABLE_EXTENSION%" >nul
if errorlevel 1 goto :failed

echo.
echo Portable executable on your Desktop:
echo %DESKTOP%\MCPHub-Frontend.exe
echo Chrome extension folder:
echo %PORTABLE_EXTENSION%

echo Starting the portable application...
start "" "%DESKTOP%\MCPHub-Frontend.exe"
if errorlevel 1 goto :failed

exit /b 0

:failed
echo.
echo Build failed. Review the output above.
pause
exit /b 1
