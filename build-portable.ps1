#Requires -Version 5.1

$ProjectDir = $PSScriptRoot
Set-Location -LiteralPath $ProjectDir

# Check prerequisites
if (-not (Get-Command pnpm -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: pnpm was not found on PATH." -ForegroundColor Red
    Write-Host "Install Node.js, then run: corepack enable"
    exit 1
}

if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: Rust/Cargo was not found on PATH." -ForegroundColor Red
    Write-Host "Install the official Rust MSVC toolchain from https://rustup.rs/"
    exit 1
}

Write-Host "Building the portable Windows executable..." -ForegroundColor Cyan
pnpm tauri:build
if ($LASTEXITCODE -ne 0) { exit 1 }

$Desktop = [Environment]::GetFolderPath("Desktop")
$Source = "$ProjectDir\src-tauri\target\release\MCPHub-Frontend.exe"
$Dest = "$Desktop\MCPHub-Frontend.exe"

Copy-Item -LiteralPath $Source -Destination $Dest -Force
if (-not $?) { exit 1 }

Write-Host "`nPortable executable on your Desktop:" -ForegroundColor Green
Write-Host $Dest -ForegroundColor Green
exit 0
