$ErrorActionPreference = "Stop"

$shortcutPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Startup\SimpleKioskOS.lnk"
if (Test-Path $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
    Write-Host "Disabled SimpleKioskOS startup shortcut."
} else {
    Write-Host "SimpleKioskOS startup shortcut was not present."
}

$runKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
if (Test-Path $runKey) {
    Remove-ItemProperty -Path $runKey -Name "SimpleKioskOS" -ErrorAction SilentlyContinue
    Write-Host "Disabled SimpleKioskOS machine Run entry."
}
