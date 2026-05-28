param(
    [string]$Configuration = "Release",
    [string]$InstallRoot = "$env:ProgramFiles\SimpleKioskOS"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path "$PSScriptRoot\.."
$publishRoot = Join-Path $repoRoot "artifacts\publish"
$publishDir = Join-Path $publishRoot "service"

dotnet publish (Join-Path $repoRoot "src\OTM.Service\OTM.Service.csproj") -c $Configuration -o $publishDir
dotnet publish (Join-Path $repoRoot "src\OTM.ControlPanel\OTM.ControlPanel.csproj") -c $Configuration -o (Join-Path $publishRoot "control-panel")
dotnet publish (Join-Path $repoRoot "src\OTM.KioskShell\OTM.KioskShell.csproj") -c $Configuration -o (Join-Path $publishRoot "kiosk-shell")
dotnet publish (Join-Path $repoRoot "src\OTM.RecoveryTool\OTM.RecoveryTool.csproj") -c $Configuration -o (Join-Path $publishRoot "recovery")

New-Item -ItemType Directory -Force -Path (Join-Path $InstallRoot "Service"), (Join-Path $InstallRoot "ControlPanel"), (Join-Path $InstallRoot "KioskShell"), (Join-Path $InstallRoot "Recovery") | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination (Join-Path $InstallRoot "Service") -Recurse -Force
Copy-Item -Path (Join-Path $publishRoot "control-panel\*") -Destination (Join-Path $InstallRoot "ControlPanel") -Recurse -Force
Copy-Item -Path (Join-Path $publishRoot "kiosk-shell\*") -Destination (Join-Path $InstallRoot "KioskShell") -Recurse -Force
Copy-Item -Path (Join-Path $publishRoot "recovery\*") -Destination (Join-Path $InstallRoot "Recovery") -Recurse -Force

$serviceExe = Join-Path $InstallRoot "Service\OTM.Service.exe"
if (Get-Service -Name "OTMKioskService" -ErrorAction SilentlyContinue) {
    Stop-Service -Name "OTMKioskService" -ErrorAction SilentlyContinue
    sc.exe delete "OTMKioskService" | Out-Null
    Start-Sleep -Seconds 2
}

sc.exe create "OTMKioskService" binPath= "`"$serviceExe`"" start= auto DisplayName= "OTM Kiosk Service" | Out-Null
sc.exe description "OTMKioskService" "Local-first Windows lockdown and kiosk enforcement service." | Out-Null
netsh advfirewall firewall add rule name="SimpleKioskOS Local API" dir=in action=allow protocol=TCP localport=47821 profile=domain,private | Out-Null
Start-Service -Name "OTMKioskService"

Write-Host "Installed OTM Kiosk Service."
Write-Host "Fullscreen shell: $(Join-Path $InstallRoot "KioskShell\OTM.KioskShell.exe")"
Write-Host "Local API: http://localhost:47821"
Write-Host "First-run PIN: 123456"
Write-Host "Database: $env:ProgramData\OTM Kiosk\otm-kiosk.db"
Write-Host "Recovery key file: $env:ProgramData\OTM Kiosk\first-run-recovery-key.txt"
