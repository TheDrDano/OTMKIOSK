param(
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

$policyRoots = @(
    "HKLM:\SOFTWARE\Policies\Microsoft\Edge",
    "HKLM:\SOFTWARE\Policies\Google\Chrome",
    "HKCU:\SOFTWARE\Policies\Microsoft\Edge",
    "HKCU:\SOFTWARE\Policies\Google\Chrome"
)

$listNames = @(
    "URLBlocklist",
    "URLAllowlist",
    "URLBlacklist",
    "URLWhitelist"
)

$valueNames = @(
    "DownloadRestrictions",
    "SafeBrowsingAllowlistDomains"
)

foreach ($root in $policyRoots) {
    if (-not (Test-Path $root)) {
        continue
    }

    try {
        foreach ($valueName in $valueNames) {
            if ((Get-ItemProperty -Path $root -Name $valueName -ErrorAction SilentlyContinue) -ne $null) {
                Remove-ItemProperty -Path $root -Name $valueName -ErrorAction Stop
            }
        }

        foreach ($listName in $listNames) {
            $path = Join-Path $root $listName
            if (Test-Path $path) {
                Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
            }
        }

        if (-not $Quiet) {
            Write-Host "Cleared SimpleKioskOS browser policy values at $root"
        }
    } catch {
        if (-not $Quiet) {
            Write-Warning "Could not clear $root. Run PowerShell as Administrator to clear machine-wide browser policies. Details: $($_.Exception.Message)"
        }
    }
}

if (-not $Quiet) {
    Write-Host "Edge/Chrome URL and download restrictions have been removed from HKLM and HKCU policy roots."
    Write-Host "Close every Edge/Chrome window, then reopen the browser or use edge://policy / chrome://policy and reload policies."
}
