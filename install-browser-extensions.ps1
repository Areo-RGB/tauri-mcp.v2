$ErrorActionPreference = "Stop"

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$extensionsDir = Join-Path $projectDir "winforms\chrome-extension"

function Install-ChromiumExtension {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$ExtensionId,
        [Parameter(Mandatory = $true)][string]$FolderName
    )

    $targetDir = Join-Path $extensionsDir $FolderName
    $manifestPath = Join-Path $targetDir "manifest.json"
    if (Test-Path -LiteralPath $manifestPath) {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        Write-Host "$Name $($manifest.version) is already available."
        return
    }

    $temporaryDir = Join-Path ([System.IO.Path]::GetTempPath()) ("mcphub-extension-" + [guid]::NewGuid())
    $packagePath = Join-Path $temporaryDir "extension.crx"
    $archivePath = Join-Path $temporaryDir "extension.zip"
    $extractDir = Join-Path $temporaryDir "extract"

    try {
        New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
        Write-Host "Downloading official $Name Chromium package..."
        $packageUrl = "https://clients2.google.com/service/update2/crx?response=redirect&prodversion=140.0.0.0&acceptformat=crx3&x=id%3D$ExtensionId%26uc"
        Invoke-WebRequest -Uri $packageUrl -OutFile $packagePath -Headers @{ "User-Agent" = "MCPHub-Portable-Builder" }

        $package = [System.IO.File]::ReadAllBytes($packagePath)
        if ($package.Length -lt 13 -or [Text.Encoding]::ASCII.GetString($package, 0, 4) -ne "Cr24") { throw "$Name download is not a valid CRX package." }
        $crxVersion = [BitConverter]::ToUInt32($package, 4)
        if ($crxVersion -ne 3) { throw "Expected CRX3 for $Name but received CRX$crxVersion." }
        $headerLength = [BitConverter]::ToUInt32($package, 8)
        $zipOffset = 12 + $headerLength
        $zipBytes = New-Object byte[] ($package.Length - $zipOffset)
        [Array]::Copy($package, $zipOffset, $zipBytes, 0, $zipBytes.Length)
        [System.IO.File]::WriteAllBytes($archivePath, $zipBytes)
        Expand-Archive -LiteralPath $archivePath -DestinationPath $extractDir -Force

        $manifest = Get-ChildItem -LiteralPath $extractDir -Filter manifest.json -File -Recurse | Select-Object -First 1
        if (-not $manifest) { throw "$Name package contains no manifest.json." }
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        Copy-Item -Path (Join-Path $manifest.Directory.FullName "*") -Destination $targetDir -Recurse -Force
        $installed = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        Write-Host "Installed $Name $($installed.version), Manifest V$($installed.manifest_version)."
    }
    finally {
        if (Test-Path -LiteralPath $temporaryDir) { Remove-Item -LiteralPath $temporaryDir -Recurse -Force }
    }
}

New-Item -ItemType Directory -Path $extensionsDir -Force | Out-Null
Install-ChromiumExtension -Name "uBlock Origin Lite" -ExtensionId "ddkjiahejlhfcafbddmgiahcphecmpfh" -FolderName "ublock-lite"
Install-ChromiumExtension -Name "SponsorBlock" -ExtensionId "mnjggcdmjocbbbhaepdhchncahnbgone" -FolderName "sponsorblock"
