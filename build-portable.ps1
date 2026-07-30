#Requires -Version 5.1

$ErrorActionPreference = 'Stop'
$ProjectDir = $PSScriptRoot
Set-Location -LiteralPath $ProjectDir

if (-not (Get-Command pnpm -ErrorAction SilentlyContinue)) {
    throw 'pnpm was not found. Install Node.js 20 or newer, then run: corepack enable'
}

pnpm install --no-frozen-lockfile
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

pnpm electron:build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$Desktop = [Environment]::GetFolderPath('Desktop')
$Source = Join-Path $ProjectDir 'dist-electron\MCPHub-Frontend.exe'
$Destination = Join-Path $Desktop 'MCPHub-Frontend.exe'
Copy-Item -LiteralPath $Source -Destination $Destination -Force

Write-Host "`nPortable Electron executable:" -ForegroundColor Green
Write-Host $Destination -ForegroundColor Green
