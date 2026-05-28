param(
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

$policyRoots = @(
    "HKLM:\SOFTWARE\Policies\Microsoft\Edge",
    "HKLM:\SOFTWARE\Policies\Google\Chrome"
)

$listNames = @(
    "URLBlocklist",
    "URLAllowlist"
)

foreach ($root in $policyRoots) {
    if (-not (Test-Path $root)) {
        continue
    }

    Remove-ItemProperty -Path $root -Name "DownloadRestrictions" -ErrorAction SilentlyContinue

    foreach ($listName in $listNames) {
        $path = Join-Path $root $listName
        if (Test-Path $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }

    if (-not $Quiet) {
        Write-Host "Cleared SimpleKioskOS browser policy values at $root"
    }
}

if (-not $Quiet) {
    Write-Host "Edge/Chrome URL and download restrictions have been removed. Restart browsers for changes to apply."
}
