param(
    [switch]$Clear,
    [switch]$WhitelistOnly,
    [string[]]$AllowedSites = @(),
    [string[]]$BlockedSites = @("youtube.com", "youtu.be", "tiktok.com", "instagram.com", "facebook.com", "x.com", "twitter.com", "reddit.com", "discord.com")
)

$ErrorActionPreference = "Stop"

function Set-PolicyList {
    param([string]$Root, [string]$Name, [string[]]$Values)
    $path = Join-Path $Root $Name
    if (Test-Path $path) {
        Remove-Item -Path $path -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $path | Out-Null
    $index = 1
    foreach ($value in $Values) {
        New-ItemProperty -Path $path -Name "$index" -Value $value -PropertyType String -Force | Out-Null
        $index++
    }
}

$edge = "HKLM:\SOFTWARE\Policies\Microsoft\Edge"
$chrome = "HKLM:\SOFTWARE\Policies\Google\Chrome"

if ($Clear) {
    & "$PSScriptRoot\clear-browser-policies.ps1"
    return
}

foreach ($root in @($edge, $chrome)) {
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    New-ItemProperty -Path $root -Name "DownloadRestrictions" -Value 3 -PropertyType DWord -Force | Out-Null
    Set-PolicyList -Root $root -Name "URLBlocklist" -Values $BlockedSites
    if ($WhitelistOnly) {
        Set-PolicyList -Root $root -Name "URLAllowlist" -Values $AllowedSites
        Set-PolicyList -Root $root -Name "URLBlocklist" -Values @("*")
    }
}

Write-Host "Applied Edge/Chrome policy registry settings. Restart browsers for changes to apply."
