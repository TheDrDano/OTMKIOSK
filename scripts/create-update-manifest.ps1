param(
    [Parameter(Mandatory)]
    [string]$Version,
    [Parameter(Mandatory)]
    [string]$Repository,
    [Parameter(Mandatory)]
    [string]$Tag,
    [Parameter(Mandatory)]
    [string]$InstallerPath,
    [string]$Channel = "stable",
    [string]$OutputPath = "artifacts\release\update-manifest.json",
    [string]$ReleaseNotes = "SimpleKioskOS release"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $InstallerPath)) {
    throw "Station installer not found: $InstallerPath"
}

$installer = Get-Item $InstallerPath
$normalizedTag = $Tag.Trim()
if ($normalizedTag.StartsWith("V", [System.StringComparison]::Ordinal)) {
    $normalizedTag = "v" + $normalizedTag.Substring(1)
}

$installerUrl = "https://github.com/$Repository/releases/latest/download/$($installer.Name)"
$sha256 = (Get-FileHash -Path $installer.FullName -Algorithm SHA256).Hash.ToLowerInvariant()

$manifest = [ordered]@{
    product = "SimpleKioskOS"
    version = $Version
    channel = $Channel
    releaseTag = $normalizedTag
    publishedAt = (Get-Date).ToUniversalTime().ToString("o")
    installerUrl = $installerUrl
    sha256 = $sha256
    installerName = $installer.Name
    installerSize = $installer.Length
    releaseNotes = $ReleaseNotes
    updateBehavior = "prompt-and-download-only"
    autoInstallEnabled = $false
    releaseUrl = "https://github.com/$Repository/releases/tag/$normalizedTag"
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $OutputPath -Encoding UTF8
Write-Host "Update manifest written to $OutputPath"
