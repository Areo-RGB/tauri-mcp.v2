param(
  [Parameter(Mandatory = $true)] [string] $ExtensionId,
  [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$cargo = Join-Path $env:USERPROFILE '.cargo\bin\cargo.exe'
$profile = if ($Configuration -eq 'Release') { 'release' } else { 'debug' }
$buildArgs = @('build', '--manifest-path', (Join-Path $projectRoot 'src-tauri\Cargo.toml'), '--bin', 'chapter-clipper-native-host')
if ($Configuration -eq 'Release') { $buildArgs += '--release' }
& $cargo @buildArgs

$hostPath = Join-Path $projectRoot "src-tauri\target\$profile\chapter-clipper-native-host.exe"
if (-not (Test-Path $hostPath)) { throw "Native host was not built: $hostPath" }
$manifestDir = Join-Path $env:LOCALAPPDATA 'MCPHub\NativeMessagingHosts'
New-Item -ItemType Directory -Force -Path $manifestDir | Out-Null
$manifestPath = Join-Path $manifestDir 'com.mcphub.chapter_clipper.json'
$manifestJson = @{
  name = 'com.mcphub.chapter_clipper'
  description = 'MCPHub Chapter Clipper native yt-dlp and ffmpeg host'
  path = $hostPath
  type = 'stdio'
  allowed_origins = @("chrome-extension://$ExtensionId/")
} | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText($manifestPath, $manifestJson, [System.Text.UTF8Encoding]::new($false))

$registryPath = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.mcphub.chapter_clipper'
New-Item -Force -Path $registryPath | Out-Null
Set-Item -Path $registryPath -Value $manifestPath
Write-Host "Installed native host for extension $ExtensionId"
Write-Host "Manifest: $manifestPath"
Write-Host "Executable: $hostPath"
