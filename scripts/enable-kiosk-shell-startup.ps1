param(
    [string]$ShellPath = "$env:ProgramFiles\OTM Kiosk\KioskShell\OTM.KioskShell.exe"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ShellPath)) {
    throw "Kiosk shell was not found at $ShellPath"
}

$startupPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Startup"
New-Item -ItemType Directory -Force -Path $startupPath | Out-Null

$shortcutPath = Join-Path $startupPath "OTM Kiosk Shell.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $ShellPath
$shortcut.WorkingDirectory = Split-Path $ShellPath
$shortcut.Description = "Start the OTM Kiosk fullscreen shell at sign-in."
$shortcut.Save()

Write-Host "Enabled OTM Kiosk Shell startup: $shortcutPath"
