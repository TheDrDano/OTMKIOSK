param(
    [string]$ShellPath = "$env:ProgramFiles\SimpleKioskOS\KioskShell\OTM.KioskShell.exe"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ShellPath)) {
    throw "Kiosk shell was not found at $ShellPath"
}

$startupPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Startup"
New-Item -ItemType Directory -Force -Path $startupPath | Out-Null

$shortcutPath = Join-Path $startupPath "SimpleKioskOS.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $ShellPath
$shortcut.WorkingDirectory = Split-Path $ShellPath
$shortcut.Description = "Start the SimpleKioskOS fullscreen shell at sign-in."
$shortcut.Save()

$runKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
New-Item -Path $runKey -Force | Out-Null
Set-ItemProperty -Path $runKey -Name "SimpleKioskOS" -Value "`"$ShellPath`""

Write-Host "Enabled SimpleKioskOS startup: $shortcutPath"
Write-Host "Enabled SimpleKioskOS machine Run entry: $runKey\SimpleKioskOS"
