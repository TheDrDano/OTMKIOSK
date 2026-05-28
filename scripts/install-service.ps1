param(
    [string]$Configuration = "Release",
    [string]$InstallRoot = "$env:ProgramFiles\OTM Kiosk"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path "$PSScriptRoot\.."
$publishDir = Join-Path $repoRoot "artifacts\publish\service"

dotnet publish (Join-Path $repoRoot "src\OTM.Service\OTM.Service.csproj") -c $Configuration -o $publishDir
dotnet publish (Join-Path $repoRoot "src\OTM.ControlPanel\OTM.ControlPanel.csproj") -c $Configuration -o (Join-Path $repoRoot "artifacts\publish\control-panel")
dotnet publish (Join-Path $repoRoot "src\OTM.RecoveryTool\OTM.RecoveryTool.csproj") -c $Configuration -o (Join-Path $repoRoot "artifacts\publish\recovery")

New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $InstallRoot -Recurse -Force

$serviceExe = Join-Path $InstallRoot "OTM.Service.exe"
if (Get-Service -Name "OTMKioskService" -ErrorAction SilentlyContinue) {
    Stop-Service -Name "OTMKioskService" -ErrorAction SilentlyContinue
    sc.exe delete "OTMKioskService" | Out-Null
    Start-Sleep -Seconds 2
}

sc.exe create "OTMKioskService" binPath= "`"$serviceExe`"" start= auto DisplayName= "OTM Kiosk Service" | Out-Null
sc.exe description "OTMKioskService" "Local-first Windows lockdown and kiosk enforcement service." | Out-Null
Start-Service -Name "OTMKioskService"

Write-Host "Installed OTM Kiosk Service."
Write-Host "Local manager: http://localhost:47821"
Write-Host "First-run PIN: 123456"
Write-Host "Database: $env:ProgramData\OTM Kiosk\otm-kiosk.db"
Write-Host "Recovery key file: $env:ProgramData\OTM Kiosk\first-run-recovery-key.txt"
