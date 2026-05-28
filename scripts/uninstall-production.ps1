param(
    [string]$InstallRoot = "$env:ProgramFiles\OTM Kiosk",
    [switch]$RemoveData
)

$ErrorActionPreference = "Stop"

$uninstaller = Join-Path $InstallRoot "unins000.exe"

if (Test-Path $uninstaller) {
    Start-Process -FilePath $uninstaller -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -Wait
    Write-Host "Ran installed OTM Kiosk uninstaller."
} elseif (Get-Service -Name "OTMKioskService" -ErrorAction SilentlyContinue) {
    Stop-Service -Name "OTMKioskService" -Force -ErrorAction SilentlyContinue
    sc.exe delete "OTMKioskService" | Out-Null
    Write-Host "Removed OTMKioskService."
} else {
    Write-Host "OTM Kiosk does not appear to be installed."
}

if ($RemoveData) {
    $dataRoot = "$env:ProgramData\OTM Kiosk"
    if (Test-Path $dataRoot) {
        $resolved = (Resolve-Path $dataRoot).Path
        $programData = [Environment]::GetFolderPath("CommonApplicationData").TrimEnd("\") + "\"
        if (-not $resolved.StartsWith($programData, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove '$resolved' because it is outside ProgramData."
        }

        Remove-Item -LiteralPath $resolved -Recurse -Force
        Write-Host "Removed local data at $dataRoot."
    }
} else {
    Write-Host "Preserved local data at $env:ProgramData\OTM Kiosk."
}

Write-Host "OTM Kiosk production uninstall complete."
