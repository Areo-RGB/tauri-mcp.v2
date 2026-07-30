param([Parameter(Mandatory = $true)][string]$ExtensionId)
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'chapter-clipper-native-host.exe'
if (-not (Test-Path -LiteralPath $source)) {
  $source = Join-Path $PSScriptRoot 'dist-portable\MCPHub-WinForms\chapter-clipper-native-host.exe'
}
if (-not (Test-Path -LiteralPath $source)) { throw "Build the portable folder first; native host was not found." }

$hostDir = Join-Path $env:LOCALAPPDATA 'MCPHub\NativeMessagingHosts'
New-Item -ItemType Directory -Force -Path $hostDir | Out-Null
$hostPath = Join-Path $hostDir 'chapter-clipper-native-host.exe'
Copy-Item -LiteralPath $source -Destination $hostPath -Force
$manifestPath = Join-Path $hostDir 'com.mcphub.chapter_clipper.json'
@{
  name = 'com.mcphub.chapter_clipper'
  description = 'MCPHub Chapter Clipper C# native host'
  path = $hostPath
  type = 'stdio'
  allowed_origins = @("chrome-extension://$ExtensionId/")
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

$registryPath = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.mcphub.chapter_clipper'
New-Item -Force -Path $registryPath | Out-Null
Set-Item -Path $registryPath -Value $manifestPath
Write-Host "Installed MCPHub native host for extension $ExtensionId" -ForegroundColor Green
Write-Host "Manifest: $manifestPath"
Write-Host "Executable: $hostPath"
