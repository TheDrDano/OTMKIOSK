param(
    [switch]$RemoveData
)

$ErrorActionPreference = "Stop"

if (Get-Service -Name "OTMKioskService" -ErrorAction SilentlyContinue) {
    Stop-Service -Name "OTMKioskService" -ErrorAction SilentlyContinue
    sc.exe delete "OTMKioskService" | Out-Null
    & "$PSScriptRoot\clear-browser-policies.ps1" -Quiet
    netsh advfirewall firewall delete rule name="SimpleKioskOS Local API" | Out-Null
    Write-Host "Removed OTMKioskService."
} else {
    Write-Host "OTMKioskService is not installed."
}

if ($RemoveData) {
    $dataRoot = "$env:ProgramData\OTM Kiosk"
    if (Test-Path $dataRoot) {
        Remove-Item -LiteralPath $dataRoot -Recurse -Force
        Write-Host "Removed local data at $dataRoot."
    }
}
