$ErrorActionPreference = "Stop"

$shortcutPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Startup\SimpleKioskOS.lnk"
if (Test-Path $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
    Write-Host "Disabled SimpleKioskOS startup."
} else {
    Write-Host "SimpleKioskOS startup shortcut was not present."
}
