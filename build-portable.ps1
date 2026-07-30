#Requires -Version 5.1
param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$solution = Join-Path $projectRoot 'winforms\MCPHub.slnx'
$output = Join-Path $projectRoot 'dist-portable\MCPHub-WinForms'
$hostTemp = Join-Path $env:TEMP 'mcphub-native-host-publish'

if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
if (Test-Path -LiteralPath $hostTemp) { Remove-Item -LiteralPath $hostTemp -Recurse -Force }
New-Item -ItemType Directory -Force -Path $output, $hostTemp | Out-Null

dotnet restore $solution
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (-not $SkipTests) {
  dotnet test $solution --no-restore --configuration Release
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

dotnet publish (Join-Path $projectRoot 'winforms\MCPHub.App\MCPHub.App.csproj') --configuration Release --runtime win-x64 --self-contained true --output $output -p:PublishSingleFile=true -p:PublishTrimmed=false -p:DebugType=None
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet publish (Join-Path $projectRoot 'winforms\MCPHub.NativeHost\MCPHub.NativeHost.csproj') --configuration Release --runtime win-x64 --self-contained true --output $hostTemp -p:PublishSingleFile=true -p:PublishTrimmed=false -p:DebugType=None
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item -LiteralPath (Join-Path $hostTemp 'chapter-clipper-native-host.exe') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'winforms\chrome-extension\chapter-clipper') -Destination (Join-Path $output 'chrome-extension') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'install-chrome-native-host.ps1') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'winforms\oauth-config.example.json') -Destination $output -Force
$oauthConfig = Join-Path $projectRoot 'winforms\client_secret.json'
if (Test-Path -LiteralPath $oauthConfig) { Copy-Item -LiteralPath $oauthConfig -Destination (Join-Path $output 'client_secret.json') -Force }
Remove-Item -LiteralPath $hostTemp -Recurse -Force
Write-Host "Portable WinForms build: $output" -ForegroundColor Green
