$ErrorActionPreference = "Stop"

$shortcutPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Startup\OTM Kiosk Shell.lnk"
if (Test-Path $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
    Write-Host "Disabled OTM Kiosk Shell startup."
} else {
    Write-Host "OTM Kiosk Shell startup shortcut was not present."
}
