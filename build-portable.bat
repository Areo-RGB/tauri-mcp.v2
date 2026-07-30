@echo off
setlocal

set "PROJECT_DIR=%~dp0"
cd /d "%PROJECT_DIR%"

echo Stopping existing MCPHub Tools instances...
taskkill /f /im "MCPHub-Frontend.exe" >nul 2>nul
taskkill /f /im "MCPHub Frontend.exe" >nul 2>nul

where pnpm >nul 2>nul
if errorlevel 1 (
  echo ERROR: pnpm was not found on PATH.
  echo Install Node.js 20 or newer, then run: corepack enable
  pause
  exit /b 1
)

echo Installing Electron and frontend dependencies...
call pnpm install --no-frozen-lockfile
if errorlevel 1 goto :failed

echo Building the portable Electron executable...
call pnpm electron:build
if errorlevel 1 goto :failed

set "DESKTOP=%USERPROFILE%\Desktop"
copy /y "dist-electron\MCPHub-Frontend.exe" "%DESKTOP%\MCPHub-Frontend.exe" >nul
if errorlevel 1 goto :failed

set "PORTABLE_EXTENSION=%DESKTOP%\MCPHub-Frontend-extension"
if exist "%PORTABLE_EXTENSION%" rmdir /s /q "%PORTABLE_EXTENSION%"
xcopy /e /i /y "extensions\chapter-clipper" "%PORTABLE_EXTENSION%" >nul
if errorlevel 1 goto :failed

echo.
echo Portable Electron executable:
echo %DESKTOP%\MCPHub-Frontend.exe
echo Chrome extension folder:
echo %PORTABLE_EXTENSION%
exit /b 0

:failed
echo.
echo Build failed. Review the output above.
pause
exit /b 1
