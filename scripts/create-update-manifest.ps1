param(
    [Parameter(Mandatory)]
    [string]$Version,
    [Parameter(Mandatory)]
    [string]$Repository,
    [Parameter(Mandatory)]
    [string]$Tag,
    [Parameter(Mandatory)]
    [string]$InstallerPath,
    [string]$ManagerInstallerPath,
    [string]$Channel = "stable",
    [string]$OutputPath = "artifacts\release\update-manifest.json",
    [string]$ReleaseNotes = "SimpleKioskOS release"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $InstallerPath)) {
    throw "Station installer not found: $InstallerPath"
}

$installer = Get-Item $InstallerPath
$installerUrl = "https://github.com/$Repository/releases/latest/download/$($installer.Name)"
$sha256 = (Get-FileHash -Path $installer.FullName -Algorithm SHA256).Hash.ToLowerInvariant()

$manifest = [ordered]@{
    version = $Version
    channel = $Channel
    releaseTag = $Tag
    installerUrl = $installerUrl
    sha256 = $sha256
    releaseNotes = $ReleaseNotes
}

if ($ManagerInstallerPath -and (Test-Path $ManagerInstallerPath)) {
    $managerInstaller = Get-Item $ManagerInstallerPath
    $manifest.managerInstallerUrl = "https://github.com/$Repository/releases/latest/download/$($managerInstaller.Name)"
    $manifest.managerSha256 = (Get-FileHash -Path $managerInstaller.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $OutputPath -Encoding UTF8
Write-Host "Update manifest written to $OutputPath"
