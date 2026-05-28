$ErrorActionPreference = "Stop"

if (Get-Service -Name "OTMKioskService" -ErrorAction SilentlyContinue) {
    Stop-Service -Name "OTMKioskService" -ErrorAction SilentlyContinue
    sc.exe delete "OTMKioskService" | Out-Null
    Write-Host "Removed OTMKioskService."
} else {
    Write-Host "OTMKioskService is not installed."
}
